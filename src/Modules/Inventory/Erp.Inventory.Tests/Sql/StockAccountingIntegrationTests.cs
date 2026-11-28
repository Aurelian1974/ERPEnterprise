using System.Text.Json;
using Erp.Core.Contracts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Periods;
using Erp.Inventory.Application.Costing;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Inventory.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Contarea si inchiderea pe SQL Server real (02.05, S3 3.5–3.7; cu C2 si C3 din Finante): blocul de contare al miscarilor ajunge la C2 prin
/// consumatorul Stocurilor, concordanta pe gestiune iese egala dupa contare, iar pasii Stocurilor din lista de inchidere gasesc coada de
/// recalculare, stocul negativ si descarcarea lipsa.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StockAccountingIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static long nextSource = 1_100_000;

    private async Task<int> NewDepot() =>
        (await Send(new SaveWarehouse(null, Unique("AC-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1,
            null, WarehousePurpose.Regular, null))).Id;

    private async Task<int> Variant()
    {
        var (item, variants) = await NewItem(Unique("AC"), true, "U");
        await Send(new SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.None, null, null, null, false, null));
        return variants[0];
    }

    private static StockMovementRequest Line(string type, int warehouse, int variant, decimal qty, decimal? cost = null, long? source = null, byte role = 1) =>
        new(source ?? Interlocked.Increment(ref nextSource), role, type, null, warehouse, variant, qty, cost, null, null, null, null, null, null, null);

    private Task<StockPostingResult> Post(DateOnly date, bool external, params StockMovementRequest[] lines) =>
        InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            Interlocked.Increment(ref nextSource), "T", date, date, null, external, lines), Ct));

    private Task<IReadOnlyList<ReconciliationRowDto>> Reconciliation() =>
        WithService<IPeriodCloseStore, IReadOnlyList<ReconciliationRowDto>>(s => s.ReconciliationAsync(Database.CompanyId, Today, Ct));

    private Task<CloseCheckResult> Check(string code) =>
        WithService<IEnumerable<IPeriodCloseCheck>, CloseCheckResult>(checks => checks.Single(c => c.Code == code)
            .RunAsync(Database.CompanyId, new DateOnly(Today.Year, Today.Month, 1), new DateOnly(Today.Year, Today.Month, 1).AddMonths(1).AddDays(-1), Ct));

    /// <summary>Trimite la contare evenimentele MiscareStocValidata ale gestiunii, ca procesatorul outbox-ului (consumatorul PostStockEvents).</summary>
    private async Task PostEventsOf(int warehouse)
    {
        var payloads = await QueryList("""
            SELECT Payload FROM core.Outbox WHERE EventType LIKE N'%.MiscareStocValidata' AND Payload LIKE @W ORDER BY Id
            """, new { W = $"%\"warehouseId\":{warehouse},%" });
        foreach (var payload in payloads)
        {
            var evt = JsonSerializer.Deserialize<MiscareStocValidata>(payload, Json)!;
            await InTransaction(sp => ActivatorUtilities.CreateInstance<PostStockEvents>(sp).HandleAsync(evt, Ct));
        }
    }

    private async Task<IReadOnlyList<string>> QueryList(string sql, object parameters)
    {
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(Database.ConnectionString);
        return (await Dapper.SqlMapper.QueryAsync<string>(connection, sql, parameters)).ToList();
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A10")]
    public async Task The_stock_reconciliation_is_equal_once_every_movement_is_posted()
    {
        var a = await NewDepot();
        var b = await NewDepot();
        var variant = await Variant();
        await Post(Today.AddDays(-2), false, Line("RECEIPT_PURCHASE", a, variant, 10, 10));
        var line = Interlocked.Increment(ref nextSource);
        await Post(Today.AddDays(-1), false, Line("TRANSFER_OUT", a, variant, -4, source: line), Line("TRANSFER_IN", b, variant, 4, source: line, role: 2));
        await Post(Today, false, Line("RETURN_TO_SUPPLIER", a, variant, -1));

        var before = (await Reconciliation()).Where(r => r.Area == 4 && (r.KeyId == a || r.KeyId == b)).OrderBy(r => r.KeyId == b).ToList();
        Assert.Equal([("0.00", "50.00"), ("0.00", "40.00")], before.Select(r => (r.Ledger, r.Subledger)));

        await PostEventsOf(a);
        await PostEventsOf(b);

        Assert.DoesNotContain(await Reconciliation(), r => r.Area == 4 && (r.KeyId == a || r.KeyId == b));
        Assert.Equal(50m, await Query<decimal>("""
            SELECT SUM(l.DebitRon - l.CreditRon) FROM fin.JournalLine AS l JOIN fin.GlAccount AS g ON g.Id = l.AccountId
            WHERE l.WarehouseId = @W AND g.Code = N'371'
            """, new { W = a }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A9")]
    public async Task The_month_does_not_close_with_keys_waiting_for_cost_recalculation()
    {
        var warehouse = await NewDepot();
        var variant = await Variant();
        await Execute("""
            INSERT INTO inv.CostRecalcQueue (CompanyId, WarehouseId, ItemVariantId, FromDate) VALUES (@C, @W, @V, @D)
            """, new { C = Database.CompanyId, W = warehouse, V = variant, D = Today.ToDateTime(TimeOnly.MinValue) });
        try
        {
            var result = await Check("COST_RECALC_PENDING");
            Assert.Equal(CloseCheckOutcome.Failed, result.Outcome);
            Assert.Contains(result.Issues, i => i.DocumentRef == variant.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var check = await WithService<IEnumerable<IPeriodCloseCheck>, IPeriodCloseCheck>(c => Task.FromResult(c.Single(x => x.Code == "COST_RECALC_PENDING")));
            Assert.False(check.AllowOverride);
            var run = new CloseRunDto(1, 1, false, DateTime.UtcNow, null, [new CloseStepDto("COST_RECALC_PENDING", "Coada de recalculare", check.Severity,
                check.AllowOverride, result.Outcome, result.Summary, result.Issues, null, null, null)]);
            Assert.Equal("INV_RECALC_PENDING", PeriodCloseService.Refusal(run).Code);
        }
        finally
        {
            await Execute("DELETE FROM inv.CostRecalcQueue WHERE WarehouseId = @W", new { W = warehouse });
        }
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-3.5")]
    public async Task Negative_stock_and_a_missing_retail_discharge_are_listed_by_the_close_steps()
    {
        var depot = await NewDepot();
        var variant = await Variant();
        await Post(Today, true, Line("POS_SALE", depot, variant, -2));
        var negative = await Check("NEGATIVE_STOCK");
        Assert.Contains(negative.Issues, i => i.DocumentRef == variant.ToString(System.Globalization.CultureInfo.InvariantCulture) && i.Amount == -2m);
        var step = await WithService<IEnumerable<IPeriodCloseCheck>, IPeriodCloseCheck>(c => Task.FromResult(c.Single(x => x.Code == "NEGATIVE_STOCK")));
        Assert.True(step.AllowOverride);

        var store = (await Send(new SaveWarehouse(null, Unique("MG-"), "Magazin", Database.ClujStoreId, WarehouseKind.Store, ValuationBasis.RetailPrice, 7, null,
            null, null, 1, null, WarehousePurpose.Regular, null))).Id;
        await Send(new SetRetailDischargeMethod(store, 1));
        var item = await Variant();
        var month = new DateOnly(Today.Year, Today.Month, 1);
        var price = await Send(new SaveRetailPriceChange(null, store, month, null, [new RetailPriceChangeLineInput(item, "12.10")], null));
        await Send(new ValidateRetailPriceChange(price.Id, price.RowVersion));
        await Post(month, false, Line("LOAD", store, item, 5, 6));
        var storeRef = store.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains((await Check("RETAIL_DISCHARGE")).Issues, i => i.DocumentRef == storeRef);

        await Send(new ValidateRetailDischarge(store, month));
        Assert.DoesNotContain((await Check("RETAIL_DISCHARGE")).Issues, i => i.DocumentRef == storeRef);
    }
}
