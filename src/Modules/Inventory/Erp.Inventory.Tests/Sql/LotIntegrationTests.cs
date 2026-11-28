using Erp.Core.Contracts;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Loturile (CKU) pe SQL Server real (02.05, S4 2, 3.2, 3.3, 3.5): crearea si adunarea pe CKU, codurile comparate exact, FEFO cu impartirea
/// liniilor, starea lotului la iesire, transferul care pastreaza lotul, lotul NEIDENTIFICAT pentru POS, costul pe lot la identificare directa.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LotIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static long nextSource = 1_500_000;

    private async Task<int> NewDepot() =>
        (await Send(new SaveWarehouse(null, Unique("LT-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1,
            null, WarehousePurpose.Regular, null))).Id;

    private async Task<(int Item, int[] Variants)> TrackedItem(TrackingMode tracking = TrackingMode.LotAndExpiry, ValuationMethod method = ValuationMethod.Fifo,
        int variants = 1)
    {
        var (item, ids) = await NewItem(Unique("LT"), true, Enumerable.Range(1, variants).Select(i => $"V{i}").ToArray());
        await Send(new SaveItemStockSettings(item, null, method, tracking, null, null, null, false, null));
        return (item, ids);
    }

    private static StockMovementRequest In(int warehouse, int variant, decimal qty, decimal cost, string lot, DateOnly? expiry, bool confirm = false) =>
        new(Interlocked.Increment(ref nextSource), 1, "LOAD", null, warehouse, variant, qty, cost, null, lot, expiry, null, null, null, null)
        {
            ConfirmNewExpiry = confirm,
        };

    private static StockMovementRequest Out(string type, int warehouse, int variant, decimal qty, int? lot = null, long? source = null, byte role = 1) =>
        new(source ?? Interlocked.Increment(ref nextSource), role, type, null, warehouse, variant, qty, null, lot, null, null, null, null, null, null);

    private Task<StockPostingResult> Post(DateOnly date, bool external, params StockMovementRequest[] lines) =>
        InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            Interlocked.Increment(ref nextSource), "T", date, date, null, external, lines), Ct));

    private Task<StockPostingResult> Post(DateOnly date, params StockMovementRequest[] lines) => Post(date, false, lines);

    private Task<int> LotId(int variant, string code) =>
        Query<int>("SELECT Id FROM inv.Lot WHERE ItemVariantId = @V AND LotCode = @C", new { V = variant, C = code });

    private Task<decimal> LotQty(int warehouse, int lot) =>
        Query<decimal>("SELECT ISNULL(SUM(QtyOnHand), 0) FROM inv.Stock WHERE WarehouseId = @W AND LotId = @L", new { W = warehouse, L = lot });

    [Fact]
    [Trait("Criteriu", "02.05-S4-A5")]
    public async Task The_same_sku_lot_and_expiry_add_up_on_one_cku_and_any_other_combination_creates_another()
    {
        var warehouse = await NewDepot();
        var (_, v) = await TrackedItem(variants: 2);
        var expiry = new DateOnly(2027, 12, 12);
        await Post(Today.AddDays(-2), In(warehouse, v[0], 10, 2m, "I123", expiry));
        await Post(Today.AddDays(-1), In(warehouse, v[0], 5, 3m, "I123", expiry));
        await Post(Today, In(warehouse, v[0], 4, 2m, "I1234", expiry.AddDays(4)), In(warehouse, v[1], 3, 2m, "I123", expiry), In(warehouse, v[0], 1, 2m, "i123", expiry));

        var cku = await Query<(int Count, decimal Qty, string Code)>("""
            SELECT COUNT(*) OVER (), (SELECT SUM(QtyOnHand) FROM inv.Stock WHERE LotId = l.Id), l.CkuCode FROM inv.Lot AS l
            WHERE l.ItemVariantId = @V AND l.LotCode = N'I123'
            """, new { V = v[0] });
        Assert.Equal(1, cku.Count);
        Assert.Equal(15m, cku.Qty);
        Assert.EndsWith("·I123·271212", cku.Code);
        Assert.Equal(2, await Query<int>("SELECT COUNT(*) FROM inv.CostLayer WHERE LotId = @L", new { L = await LotId(v[0], "I123") }));
        Assert.Equal(3, await Query<int>("SELECT COUNT(*) FROM inv.Lot WHERE ItemVariantId = @V", new { V = v[0] }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM inv.Lot WHERE ItemVariantId = @V", new { V = v[1] }));

        var mismatch = await Assert.ThrowsAsync<ErpException>(() => Post(Today, In(warehouse, v[0], 1, 2m, "I123", expiry.AddDays(1))));
        Assert.Equal("INV_LOT_EXPIRY_MISMATCH", mismatch.Code);
        await Post(Today, In(warehouse, v[0], 1, 2m, "I123", expiry.AddDays(1), confirm: true));
        Assert.Equal(2, await Query<int>("SELECT COUNT(*) FROM inv.Lot WHERE ItemVariantId = @V AND LotCode = N'I123'", new { V = v[0] }));

        var noExpiry = await Assert.ThrowsAsync<ErpException>(() => Post(Today, In(warehouse, v[0], 1, 2m, "X1", null)));
        Assert.Equal("INV_LOT_EXPIRY_REQUIRED", noExpiry.Code);
        var noLot = await Assert.ThrowsAsync<ErpException>(() => Post(Today, Out("LOAD", warehouse, v[0], 1) with { InboundUnitCost = 1 }));
        Assert.Equal("INV_LOT_REQUIRED", noLot.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A1")]
    [Trait("Criteriu", "02.05-S4-A3")]
    public async Task Fefo_splits_an_issue_on_lots_skips_quarantine_and_lots_never_go_negative()
    {
        var warehouse = await NewDepot();
        var (item, v) = await TrackedItem();
        await Post(Today.AddDays(-3), In(warehouse, v[0], 5, 1m, "A", Today.AddDays(30)), In(warehouse, v[0], 5, 1m, "B", Today.AddDays(10)));
        await Execute("UPDATE inv.ItemStockSettings SET ReceiptQuarantine = 1 WHERE ItemId = @I", new { I = item });
        await Post(Today.AddDays(-2), In(warehouse, v[0], 50, 1m, "Q", Today.AddDays(5)));
        Assert.Equal((byte)2, await Query<byte>("SELECT Status FROM inv.Lot WHERE Id = @L", new { L = await LotId(v[0], "Q") }));

        var issue = await Post(Today, Out("ISSUE_SALE", warehouse, v[0], -7));
        Assert.Equal(2, issue.Movements.Count);
        Assert.Equal((0m, 3m), (await LotQty(warehouse, await LotId(v[0], "B")), await LotQty(warehouse, await LotId(v[0], "A"))));

        var quarantined = await LotId(v[0], "Q");
        var quarantine = await Assert.ThrowsAsync<ErpException>(() => Post(Today, Out("ISSUE_SALE", warehouse, v[0], -1, quarantined)));
        Assert.Equal("INV_LOT_STATUS_FORBIDS_ISSUE", quarantine.Code);
        var short_ = await Assert.ThrowsAsync<ErpException>(() => Post(Today, Out("UNLOAD", warehouse, v[0], -4)));
        Assert.Equal("INV_LOT_INSUFFICIENT_QTY", short_.Code);
        await Post(Today, Out("RETURN_TO_SUPPLIER", warehouse, v[0], -50, quarantined));

        // Suma pe loturi = stocul variantei; niciun lot pe minus la nicio data.
        Assert.Equal(3m, await Query<decimal>("SELECT SUM(QtyOnHand) FROM inv.Stock WHERE ItemVariantId = @V", new { V = v[0] }));
        Assert.Equal(0, await Query<int>("""
            SELECT COUNT(*) FROM (SELECT LotKey, SUM(QtyIn - QtyOut) OVER (PARTITION BY LotKey ORDER BY BalanceDate) AS Balance
                                  FROM inv.StockDayBalance WHERE ItemVariantId = @V) AS b WHERE b.Balance < 0
            """, new { V = v[0] }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%MiscareStocValidata' AND Payload LIKE N'%\"lotCode\":\"B\"%' AND Payload LIKE @V",
            new { V = $"%\"itemVariantId\":{v[0]},%\"movementTypeCode\":\"ISSUE_SALE\"%" }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A6")]
    public async Task A_transfer_keeps_the_lots_at_the_destination()
    {
        var a = await NewDepot();
        var b = await NewDepot();
        var (_, v) = await TrackedItem();
        await Post(Today.AddDays(-2), In(a, v[0], 2, 1m, "E1", Today.AddDays(5)), In(a, v[0], 6, 2m, "E2", Today.AddDays(9)));
        var line = Interlocked.Increment(ref nextSource);
        await Post(Today, Out("TRANSFER_OUT", a, v[0], -5, source: line), Out("TRANSFER_IN", b, v[0], 5, source: line, role: 2));
        Assert.Equal((2m, 3m), (await LotQty(b, await LotId(v[0], "E1")), await LotQty(b, await LotId(v[0], "E2"))));
        Assert.Equal(8m, await Query<decimal>("SELECT SUM(CostOnHand) FROM inv.Stock WHERE WarehouseId = @W", new { W = b }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A11")]
    public async Task A_pos_receipt_with_an_unknown_lot_takes_fefo_and_the_rest_goes_to_the_unidentified_lot()
    {
        var warehouse = await NewDepot();
        var (_, v) = await TrackedItem();
        await Post(Today.AddDays(-1), In(warehouse, v[0], 2, 1m, "P1", Today.AddDays(20)));
        var sale = await Post(Today, true, Out("POS_SALE", warehouse, v[0], -5, lot: 999_999));
        Assert.Equal(2, sale.Movements.Count);
        var technical = await Query<int>("SELECT Id FROM inv.Lot WHERE ItemVariantId = @V AND IsTechnical = 1", new { V = v[0] });
        Assert.Equal(-3m, await LotQty(warehouse, technical));
        Assert.Equal(0m, await LotQty(warehouse, await LotId(v[0], "P1")));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A13")]
    public async Task With_specific_identification_the_issue_of_a_lot_takes_only_its_cost()
    {
        var warehouse = await NewDepot();
        var (_, v) = await TrackedItem(TrackingMode.Lot, ValuationMethod.SpecificIdentification);
        await Post(Today.AddDays(-2), In(warehouse, v[0], 10, 4m, "S1", null), In(warehouse, v[0], 10, 9m, "S2", null));
        var issue = (await Post(Today, Out("UNLOAD", warehouse, v[0], -3, await LotId(v[0], "S2")))).Movements.Single();
        Assert.Equal(-27m, issue.CostAmount);

        await using var connection = new SqlConnection(Database.ConnectionString);
        Assert.Empty(await Dapper.SqlMapper.QueryAsync(connection, "EXEC inv.usp_Stock_Verify @CompanyId = @C, @WarehouseId = @W",
            new { C = Database.CompanyId, W = warehouse }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-3.1")]
    public async Task A_generated_lot_code_gets_its_expiry_from_the_shelf_life()
    {
        var warehouse = await NewDepot();
        var (item, v) = await TrackedItem();
        await Execute("UPDATE inv.ItemStockSettings SET LotNumbering = 2, ShelfLifeDays = 30 WHERE ItemId = @I", new { I = item });
        await Post(Today, Out("LOAD", warehouse, v[0], 4) with { InboundUnitCost = 1 });
        var lot = await Query<(string Code, DateTime Expiry)>("SELECT LotCode, ExpiryDate FROM inv.Lot WHERE ItemVariantId = @V", new { V = v[0] });
        Assert.StartsWith("L", lot.Code);
        Assert.Equal(Today.AddDays(30), DateOnly.FromDateTime(lot.Expiry));
    }
}
