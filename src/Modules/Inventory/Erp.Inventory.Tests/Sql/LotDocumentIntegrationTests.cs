using System.Diagnostics;
using System.Text.Json;
using Erp.Core.Contracts;
using Erp.Finance.Contracts.Periods;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Lots;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Inventory.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Documentele de lot pe SQL Server real (02.05, S4 3.6, 3.7): retragerea care blocheaza lotul in toata firma si pastreaza clientii, schimbarea
/// lotului (SL) care corecteaza NEIDENTIFICAT fara nota contabila, pasul de inchidere pe NEIDENTIFICAT, trasabilitatea prin genealogie.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LotDocumentIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static long nextSource = 1_900_000;

    private async Task<int> NewWarehouse() =>
        (await Send(new SaveWarehouse(null, Unique("LD-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1,
            null, WarehousePurpose.Regular, null))).Id;

    private async Task<int> Transit() => (await Send(new GetWarehouses(Kind: WarehouseKind.Transit))).Warehouses.Single().Id;

    private async Task<int> TrackedVariant()
    {
        var (item, ids) = await NewItem(Unique("LD"), true, "U");
        await Send(new SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.LotAndExpiry, null, null, null, false, null));
        return ids[0];
    }

    private static StockMovementRequest In(int warehouse, int variant, decimal qty, string lot, DateOnly expiry) =>
        new(Interlocked.Increment(ref nextSource), 1, "LOAD", null, warehouse, variant, qty, 1m, null, lot, expiry, null, null, null, null);

    private static StockMovementRequest Line(string type, int warehouse, int variant, decimal qty, int? lot = null, long? source = null, byte role = 1) =>
        new(source ?? Interlocked.Increment(ref nextSource), role, type, null, warehouse, variant, qty, null, lot, null, null, null, null, null, null);

    private Task<StockPostingResult> Post(DateOnly date, bool external, int? partner, params StockMovementRequest[] lines) =>
        InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            Interlocked.Increment(ref nextSource), "T", date, date, partner, external, lines), Ct));

    private Task<int> LotId(int variant, string code) =>
        Query<int>("SELECT Id FROM inv.Lot WHERE ItemVariantId = @V AND LotCode = @C", new { V = variant, C = code });

    private Task<decimal> LotQty(int warehouse, int lot) =>
        Query<decimal>("SELECT ISNULL(SUM(QtyOnHand), 0) FROM inv.Stock WHERE WarehouseId = @W AND LotId = @L", new { W = warehouse, L = lot });

    private Task<CloseCheckResult> Check(string code) =>
        WithService<IEnumerable<IPeriodCloseCheck>, CloseCheckResult>(checks => checks.Single(c => c.Code == code)
            .RunAsync(Database.CompanyId, new DateOnly(Today.Year, Today.Month, 1), new DateOnly(Today.Year, Today.Month, 1).AddMonths(1).AddDays(-1), Ct));

    [Fact]
    [Trait("Criteriu", "02.05-S4-A9")]
    public async Task A_validated_recall_blocks_the_lot_in_every_warehouse_and_keeps_the_customers()
    {
        var depot = await NewWarehouse();
        var transit = await Transit();
        var variant = await TrackedVariant();
        await Post(Today.AddDays(-2), false, null, In(depot, variant, 10, "R1", Today.AddDays(90)));
        var lot = await LotId(variant, "R1");
        var line = Interlocked.Increment(ref nextSource);
        await Post(Today.AddDays(-1), false, null, Line("TRANSFER_OUT", depot, variant, -3, source: line), Line("TRANSFER_IN", transit, variant, 3, source: line, role: 2));
        await Post(Today, false, Database.LogisticPartnerId, Line("ISSUE_SALE", depot, variant, -2, lot));

        var draft = await Send(new SaveRecall(null, Today, 1, "Contaminare semnalată de furnizor", null, null, [lot], null));
        await Send(new ValidateRecall(draft.Id, draft.RowVersion));

        Assert.Equal(LotStatus.Recalled, (await Send(new GetLot(lot))).Status);
        var recall = await Send(new GetRecall(draft.Id));
        Assert.Equal((byte)2, recall.Status);
        Assert.NotNull(recall.Number);
        var customer = Assert.Single(recall.Customers);
        Assert.Equal((Database.LogisticPartnerId, 2m), (customer.PartnerId ?? 0, Quantity.ParseOptional(customer.QtyBase) ?? 0));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%RetragereValidata' AND Payload LIKE @R",
            new { R = $"%\"recallId\":{draft.Id},%" }));

        var issue = await Assert.ThrowsAsync<ErpException>(() => Post(Today, false, null, Line("ISSUE_SALE", depot, variant, -1, lot)));
        Assert.Equal("INV_LOT_STATUS_FORBIDS_ISSUE", issue.Code);
        var trace = await Send(new TraceLot(lot));
        Assert.Equal([(depot, 5m), (transit, 3m)], trace.StockNow.OrderBy(s => s.WarehouseId != depot)
            .Select(s => (s.WarehouseId, Quantity.ParseOptional(s.QtyOnHand) ?? 0)));
        Assert.Contains(trace.Forward, m => m.MovementTypeCode == "ISSUE_SALE" && m.PartnerId == Database.LogisticPartnerId);

        var close = await Assert.ThrowsAsync<ErpException>(() => Send(new CloseRecall(recall.Id, recall.RowVersion)));
        Assert.Equal("INV_RECALL_OPEN_STOCK", close.Code);
        Assert.True(await Send(new SetRecallOutcome(customer.Id, 1, "Returnat cu aviz")));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A11")]
    [Trait("Criteriu", "02.05-S4-3.7")]
    public async Task The_lot_change_document_clears_the_unidentified_lot_without_an_accounting_entry()
    {
        var warehouse = await NewWarehouse();
        var variant = await TrackedVariant();
        await Post(Today.AddDays(-1), false, null, In(warehouse, variant, 2, "P1", Today.AddDays(20)));
        await Post(Today, true, null, Line("POS_SALE", warehouse, variant, -5));
        var technical = await Query<int>("SELECT Id FROM inv.Lot WHERE ItemVariantId = @V AND IsTechnical = 1", new { V = variant });
        Assert.Equal(-3m, await LotQty(warehouse, technical));
        Assert.Equal(CloseCheckOutcome.Failed, (await Check("UNIDENTIFIED_LOT")).Outcome);

        await Post(Today, false, null, In(warehouse, variant, 10, "P2", Today.AddDays(40)));
        var p2 = await LotId(variant, "P2");
        var draft = await Send(new SaveLotChange(null, warehouse, null, Today, "Corectare bon POS", [new LotChangeLineInput(variant, p2, technical, null, null, "3")],
            null));
        await Send(new ValidateLotChange(draft.Id, draft.RowVersion));

        Assert.Equal((0m, 7m), (await LotQty(warehouse, technical), await LotQty(warehouse, p2)));
        Assert.Equal(CloseCheckOutcome.Ok, (await Check("UNIDENTIFIED_LOT")).Outcome);
        Assert.Equal((byte)2, (await Send(new GetLotChange(draft.Id))).Status);

        // Perechea LOT_CHANGE_OUT / LOT_CHANGE_IN e la cost (suma zero) si nu ajunge la contabilitate.
        Assert.Equal(0m, await Query<decimal>("""
            SELECT SUM(m.CostAmount) FROM inv.StockMovement AS m WHERE m.SourceDocumentType = 'SL' AND m.SourceDocumentId = @Id
            """, new { draft.Id }));
        var entries = await Query<int>("SELECT COUNT(*) FROM fin.JournalEntry");
        var payload = await Query<string>("""
            SELECT TOP (1) Payload FROM core.Outbox WHERE EventType LIKE N'%.MiscareStocValidata' AND Payload LIKE N'%"documentType":"SL"%' AND Payload LIKE @Id
            ORDER BY Id DESC
            """, new { Id = $"%\"documentId\":{draft.Id},%" });
        await InTransaction(sp => ActivatorUtilities.CreateInstance<PostStockEvents>(sp).HandleAsync(JsonSerializer.Deserialize<MiscareStocValidata>(payload, Json)!, Ct));
        Assert.Equal(entries, await Query<int>("SELECT COUNT(*) FROM fin.JournalEntry"));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A10")]
    [Trait("Criteriu", "02.05-S4-3.6")]
    public async Task The_trace_follows_the_genealogy_and_answers_quickly_for_a_lot_with_fifty_thousand_movements()
    {
        var warehouse = await NewWarehouse();
        var variant = await TrackedVariant();
        await Post(Today.AddDays(-1), false, null, In(warehouse, variant, 100_000, "G1", Today.AddDays(60)));
        var parent = await LotId(variant, "G1");
        var draft = await Send(new SaveLotChange(null, warehouse, null, Today, null, [new LotChangeLineInput(variant, parent, null, "G1-R", Today.AddDays(30), "4")],
            null));
        await Send(new ValidateLotChange(draft.Id, draft.RowVersion));
        var child = await LotId(variant, "G1-R");
        Assert.Equal(child, (await Send(new GetLotChange(draft.Id))).Lines.Single().ToLotId);

        var back = await Send(new TraceLot(child));
        Assert.Equal(parent, Assert.Single(back.Back).LotId);
        var forward = await Send(new TraceLot(parent));
        Assert.Contains(forward.Forward, m => m.Depth == 1 && m.LotId == child && m.MovementTypeCode == "LOT_CHANGE_IN");

        // 50.000 de miscari pe lot (copii ale iesirii, cu linia sursa unica), apoi trasabilitatea sub 2 secunde.
        await Execute("""
            DECLARE @Columns nvarchar(max) = (SELECT STRING_AGG(QUOTENAME(name), N', ') FROM sys.columns
                WHERE object_id = OBJECT_ID(N'inv.StockMovement') AND is_identity = 0 AND is_computed = 0 AND name <> N'SourceLineId');
            DECLARE @Sql nvarchar(max) = CONCAT(N'INSERT INTO inv.StockMovement (', @Columns, N', SourceLineId) SELECT ', @Columns,
                N', m.SourceLineId + 10000000 + n.n FROM inv.StockMovement AS m CROSS JOIN (SELECT TOP (50000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b) AS n WHERE m.LotId = @Lot AND m.SourceDocumentType = ''SL'' AND m.SourceRole = 1');
            EXEC sp_executesql @Sql, N'@Lot int', @Lot = @Lot;
            """, new { Lot = parent });
        var watch = Stopwatch.StartNew();
        var big = await Send(new TraceLot(parent));
        watch.Stop();
        Assert.True(big.Forward.Count > 50_000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Trasabilitatea a durat {watch.Elapsed.TotalMilliseconds:F0} ms.");
    }
}
