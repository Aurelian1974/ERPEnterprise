using Erp.Core.Contracts;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Registrul de miscari, soldul si portul IStockLedger pe SQL Server real (02.05, S2 2, 4.1, 4.6, 4.7): soldurile egale cu registrul,
/// concurenta pe ultimele bucati, stocul negativ pe datele ulterioare, idempotenta pe sursa, imuabilitatea, stornarea la ban, costul mediu.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StockLedgerIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static long nextSource = 1000;

    private async Task<int> NewDepot(NegativeStockPolicy policy = NegativeStockPolicy.Blocked)
    {
        var saved = await Send(new SaveWarehouse(null, Unique("SL-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null,
            null, policy, null, 1, null, WarehousePurpose.Regular, null));
        return saved.Id;
    }

    private static StockMovementRequest Line(string type, int warehouse, int variant, decimal qty, decimal? cost = null, long? source = null, byte role = 1) =>
        new(source ?? Interlocked.Increment(ref nextSource), role, type, null, warehouse, variant, qty, cost, null, null, null, null, null, null, null);

    private Task<StockPostingResult> Post(DateOnly date, params StockMovementRequest[] lines) => Post(date, false, lines);

    private Task<StockPostingResult> Post(DateOnly date, bool external, params StockMovementRequest[] lines) =>
        InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            Interlocked.Increment(ref nextSource), "T-1", date, date, null, external, lines), Ct));

    private Task<(decimal Qty, decimal Cost)> Stock(int warehouse, int variant) =>
        Query<(decimal, decimal)>("SELECT ISNULL(SUM(QtyOnHand), 0), ISNULL(SUM(CostOnHand), 0) FROM inv.Stock WHERE WarehouseId = @W AND ItemVariantId = @V",
            new { W = warehouse, V = variant });

    [Fact]
    [Trait("Criteriu", "02.05-S2-A4")]
    public async Task Replaying_the_same_source_does_not_double_the_stock()
    {
        var warehouse = await NewDepot();
        var (_, variants) = await NewItem(Unique("ID"), true, "U");
        var line = Line("LOAD", warehouse, variants[0], 10, 4.5m);
        var request = new StockPostingRequest(Database.CompanyId, "test", "TEST", 77, "T-77", Today, Today, null, false, [line]);

        var first = await InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(request, Ct));
        var second = await InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(request, Ct));

        Assert.Equal(first.Movements.Single().MovementId, second.Movements.Single().MovementId);
        Assert.Equal((10m, 45m), await Stock(warehouse, variants[0]));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%MiscareStocValidata%' AND Payload LIKE @M",
            new { M = $"%\"movementId\":{first.Movements.Single().MovementId},%" }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-A3")]
    [Trait("Criteriu", "02.05-S1-A9")]
    public async Task A_backdated_issue_that_leaves_a_later_day_negative_is_refused_unless_the_policy_allows_it()
    {
        var blocked = await NewDepot();
        var permissive = await NewDepot(NegativeStockPolicy.AllowedWithWarning);
        var (_, variants) = await NewItem(Unique("NG"), true, "U");
        foreach (var warehouse in new[] { blocked, permissive })
        {
            await Post(Today.AddDays(-20), Line("LOAD", warehouse, variants[0], 10, 2));
            await Post(Today.AddDays(-10), Line("UNLOAD", warehouse, variants[0], -8));
        }

        // Pe ziua de acum 15 zile stocul era 10, dar dupa iesire ziua de acum 10 zile ar ajunge la -3.
        var refused = await Assert.ThrowsAsync<ErpException>(() => Post(Today.AddDays(-15), Line("UNLOAD", blocked, variants[0], -5)));
        Assert.Equal("INV_STOCK_INSUFFICIENT", refused.Code);
        Assert.Contains("lipsesc 3", refused.Message);
        Assert.Equal(2m, (await Stock(blocked, variants[0])).Qty);

        var allowed = await Post(Today.AddDays(-15), Line("UNLOAD", permissive, variants[0], -5));
        Assert.True(allowed.Movements.Single().IsForcedNegative);
        Assert.Equal(-3m, (await Stock(permissive, variants[0])).Qty);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A10")]
    public async Task Tracked_variants_never_go_negative_even_with_the_permissive_policy()
    {
        var permissive = await NewDepot(NegativeStockPolicy.AllowedWithWarning);
        var (item, variants) = await NewItem(Unique("LT"), true, "U");
        await Send(new Application.Items.SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.Lot, null, null, null, false, null));
        var refused = await Assert.ThrowsAsync<ErpException>(() => Post(Today, Line("UNLOAD", permissive, variants[0], -1)));
        Assert.Equal("INV_STOCK_INSUFFICIENT", refused.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-A2")]
    public async Task Two_simultaneous_issues_of_the_last_ten_units_leave_zero_not_minus_ten()
    {
        var warehouse = await NewDepot();
        var (_, variants) = await NewItem(Unique("CC"), true, "U");
        await Post(Today, Line("LOAD", warehouse, variants[0], 10, 1));

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            try
            {
                await Post(Today, Line("UNLOAD", warehouse, variants[0], -10));
                return "ok";
            }
            catch (ErpException e)
            {
                return e.Code;
            }
        })));

        Assert.Equal(["INV_STOCK_INSUFFICIENT", "ok"], results.Order());
        Assert.Equal((0m, 0m), await Stock(warehouse, variants[0]));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-A1")]
    public async Task After_any_sequence_of_movements_the_balances_equal_the_ledger()
    {
        var a = await NewDepot(NegativeStockPolicy.AllowedWithWarning);
        var b = await NewDepot();
        var (_, variants) = await NewItem(Unique("PR"), true, "X", "Y", "Z");
        var random = new Random(26112026);
        for (var i = 0; i < 60; i++)
        {
            var variant = variants[random.Next(variants.Length)];
            var date = Today.AddDays(-random.Next(0, 25));
            var qty = random.Next(1, 20);
            switch (random.Next(4))
            {
                case 0:
                    await Post(date, Line("LOAD", a, variant, qty, random.Next(1, 100) / 4m));
                    break;
                case 1:
                    await Post(date, Line("UNLOAD", a, variant, -qty));
                    break;
                case 2:
                    var source = Interlocked.Increment(ref nextSource);
                    try
                    {
                        await Post(date, Line("TRANSFER_OUT", a, variant, -qty, source: source), Line("TRANSFER_IN", b, variant, qty, source: source, role: 2));
                    }
                    catch (ErpException e) when (e.Code == "INV_STOCK_INSUFFICIENT")
                    {
                        // Gestiunea A permite stocul negativ; refuzul poate veni doar din B, care nu iese aici.
                        throw;
                    }

                    break;
                default:
                    try
                    {
                        await Post(date, Line("UNLOAD", b, variant, -qty));
                    }
                    catch (ErpException e) when (e.Code == "INV_STOCK_INSUFFICIENT")
                    {
                        // Gestiunea B blocheaza stocul negativ: refuzul e corect si nu lasa urme.
                    }

                    break;
            }
        }

        var differences = await QueryList("EXEC inv.usp_Stock_Verify @CompanyId = @C", new { C = Database.CompanyId });
        Assert.True(differences.Count == 0, string.Join("; ", differences.Select(d => (string)Newtonsoft(d))));
        // Transferul muta valoarea fara sa o schimbe: B are cost exact cat a primit minus ce a iesit (S2-A5 pe o etapa).
        var (qtyB, costB) = await Stock(b, variants[0]);
        Assert.True(qtyB >= 0);
        Assert.Equal(costB, await Query<decimal>("SELECT ISNULL(SUM(CostAmount), 0) FROM inv.StockMovement WHERE WarehouseId = @W AND ItemVariantId = @V",
            new { W = b, V = variants[0] }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-A12")]
    public async Task Movements_cannot_be_changed_or_deleted()
    {
        var warehouse = await NewDepot();
        var (_, variants) = await NewItem(Unique("IM"), true, "U");
        var id = (await Post(Today, Line("LOAD", warehouse, variants[0], 3, 1))).Movements.Single().MovementId;

        var update = await Assert.ThrowsAsync<SqlException>(() => Execute("UPDATE inv.StockMovement SET QtyBase = 4 WHERE Id = @Id", new { Id = id }));
        Assert.Equal(54199, update.Number);
        var delete = await Assert.ThrowsAsync<SqlException>(() => Execute("DELETE FROM inv.StockMovement WHERE Id = @Id", new { Id = id }));
        Assert.Equal(54199, delete.Number);
        // Exceptia S3: doar costul, cu contextul recalcularii.
        await Execute("EXEC sys.sp_set_session_context N'inv.CostRecalc', 1; UPDATE inv.StockMovement SET UnitCost = 1.5, CostAmount = 4.5 WHERE Id = @Id;",
            new { Id = id });
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-A8")]
    public async Task Weighted_average_cost_and_reversal_back_to_the_cent()
    {
        var warehouse = await NewDepot();
        var (_, variants) = await NewItem(Unique("CMP"), true, "U");
        await Post(Today.AddDays(-2), Line("LOAD", warehouse, variants[0], 10, 5));
        await Post(Today.AddDays(-2), Line("LOAD", warehouse, variants[0], 20, 6.5m));
        var documentId = Interlocked.Increment(ref nextSource);
        var issue = await InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            documentId, "ND-1", Today, Today, null, false, [Line("UNLOAD", warehouse, variants[0], -7)]), Ct));
        Assert.Equal((6m, -42m), (issue.Movements.Single().UnitCost, issue.Movements.Single().CostAmount));
        Assert.Equal((23m, 138m), await Stock(warehouse, variants[0]));

        var reversal = await InTransaction(sp => sp.GetRequiredService<IStockLedger>().ReverseAsync(new StockReversalRequest(Database.CompanyId, "test",
            documentId, null, Today, "TEST", documentId + 100000, "ND-1S"), Ct));
        Assert.Equal((7m, 42m), (reversal.Movements.Single().QtyBase, reversal.Movements.Single().CostAmount));
        Assert.Equal((30m, 180m), await Stock(warehouse, variants[0]));
        var again = await Assert.ThrowsAsync<ErpException>(() => InTransaction(sp => sp.GetRequiredService<IStockLedger>().ReverseAsync(
            new StockReversalRequest(Database.CompanyId, "test", documentId, null, Today, "TEST", documentId + 200000, "ND-1S2"), Ct)));
        Assert.Equal("INV_REVERSAL_INVALID", again.Code);

        // Tot stocul iese cu toata valoarea, fara rest de rotunjire.
        await Post(Today, Line("UNLOAD", warehouse, variants[0], -30));
        Assert.Equal((0m, 0m), await Stock(warehouse, variants[0]));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-A9")]
    public async Task Pos_facts_on_zero_stock_are_accepted_as_forced_negative_with_an_alert()
    {
        var warehouse = await NewDepot();
        var (_, variants) = await NewItem(Unique("POS"), true, "U");
        var result = await Post(Today.AddDays(-40), true, Line("POS_SALE", warehouse, variants[0], -2));
        Assert.True(result.Movements.Single().IsForcedNegative);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%StocNegativFortat%' AND Payload LIKE @M",
            new { M = $"%\"movementId\":{result.Movements.Single().MovementId},%" }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-2")]
    public async Task Dates_rules_transit_and_custody_are_enforced()
    {
        var warehouse = await NewDepot();
        var (_, variants) = await NewItem(Unique("DT"), true, "U");
        var future = await Assert.ThrowsAsync<ErpException>(() => Post(Today.AddDays(1), Line("LOAD", warehouse, variants[0], 1, 1)));
        Assert.Equal("INV_BACKDATE_NOT_ALLOWED", future.Code);
        Permissions.Remove(Application.InventoryPermissions.MovementBackdate);
        var old = await Assert.ThrowsAsync<ErpException>(() => Post(Today.AddDays(-6), Line("LOAD", warehouse, variants[0], 1, 1)));
        Assert.Equal("INV_BACKDATE_NOT_ALLOWED", old.Code);
        await Post(Today.AddDays(-5), Line("LOAD", warehouse, variants[0], 1, 1));

        var transit = (await Send(new GetWarehouses(Kind: WarehouseKind.Transit))).Warehouses.Single().Id;
        var direct = await Assert.ThrowsAsync<ErpException>(() => Post(Today, Line("CONSUMPTION", transit, variants[0], -1)));
        Assert.Equal("INV_TRANSIT_DIRECT_USE", direct.Code);
        var custody = await Assert.ThrowsAsync<ErpException>(() => Post(Today, Line("CUSTODY_IN", warehouse, variants[0], 1, 1)));
        Assert.Equal("INV_CUSTODY_MISUSE", custody.Code);
        var (_, services) = await NewItem(Unique("SV"), false, "U");
        var notStockable = await Assert.ThrowsAsync<ErpException>(() => Post(Today, Line("LOAD", warehouse, services[0], 1, 1)));
        Assert.Equal("INV_VARIANT_NOT_STOCKABLE", notStockable.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A12")]
    [Trait("Criteriu", "02.05-S1-A3")]
    public async Task A_warehouse_with_stock_cannot_be_deactivated_and_its_kind_is_locked_after_the_first_movement()
    {
        var warehouse = await NewDepot();
        var (_, variants) = await NewItem(Unique("DA"), true, "U");
        await Post(Today, Line("LOAD", warehouse, variants[0], 1, 1));
        var detail = await Send(new GetWarehouse(warehouse));
        Assert.True(detail.HasMovements);
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new DeactivateWarehouse(warehouse, detail.RowVersion)));
        Assert.Equal("INV_WAREHOUSE_NOT_EMPTY", refused.Code);
        var kind = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveWarehouse(warehouse, detail.Code, detail.Name, detail.LocationId,
            WarehouseKind.Store, ValuationBasis.Cost, null, null, null, null, 1, null, WarehousePurpose.Regular, detail.RowVersion)));
        Assert.Equal("INV_WAREHOUSE_LOCKED_FIELD", kind.Code);
    }

    private static string Newtonsoft(dynamic row) => string.Join(",", ((IDictionary<string, object>)row).Select(p => $"{p.Key}={p.Value}"));

    private async Task<IReadOnlyList<dynamic>> QueryList(string sql, object parameters)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return (await Dapper.SqlMapper.QueryAsync(connection, sql, parameters)).ToList();
    }
}
