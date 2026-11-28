using Erp.Core.Contracts;
using Erp.Inventory.Application.Costing;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Schimbarea metodei de evaluare pe SQL Server real (02.05, S1 4.5; S1-A11): valoarea fiecarei chei ramane aceeasi la ban, straturile se
/// convertesc la data, recalcularea ulterioara nu schimba nimic, iar miscarile cu data inaintea schimbarii sunt refuzate (54011).
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ValuationChangeIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static readonly DateOnly MonthStart = new(Today.Year, Today.Month, 1);
    private static long nextSource = 1_300_000;

    private async Task<int> NewDepot() =>
        (await Send(new SaveWarehouse(null, Unique("VC-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1,
            null, WarehousePurpose.Regular, null))).Id;

    private static StockMovementRequest Line(string type, int warehouse, int variant, decimal qty, decimal? cost = null) =>
        new(Interlocked.Increment(ref nextSource), 1, type, null, warehouse, variant, qty, cost, null, null, null, null, null, null, null);

    private Task<StockPostingResult> Post(DateOnly date, params StockMovementRequest[] lines) =>
        InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            Interlocked.Increment(ref nextSource), "T", date, date, null, false, lines), Ct));

    private Task<decimal> Value(int warehouse, int variant) =>
        Query<decimal>("SELECT ISNULL(SUM(CostOnHand), 0) FROM inv.Stock WHERE WarehouseId = @W AND ItemVariantId = @V", new { W = warehouse, V = variant });

    private Task<(int Count, decimal Qty, decimal Value)> OpenLayers(int warehouse, int variant) =>
        Query<(int, decimal, decimal)>("""
            SELECT COUNT(*), ISNULL(SUM(QtyRemaining), 0), ISNULL(SUM(ValueRemaining), 0) FROM inv.CostLayer
            WHERE WarehouseId = @W AND ItemVariantId = @V AND QtyRemaining <> 0
            """, new { W = warehouse, V = variant });

    private async Task<ValuationChangeDto> Change(int item, ValuationMethod method)
    {
        var saved = await Send(new SaveValuationChange(null, method, MonthStart, "Decizia administratorului.", [item], null));
        var draft = await Send(new GetValuationChange(saved.Id));
        await Send(new ValidateValuationChange(saved.Id, saved.RowVersion));
        Assert.Equal((byte)2, (await Send(new GetValuationChange(saved.Id))).Status);
        return draft;
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A11")]
    public async Task Fifo_to_average_and_back_keeps_the_value_of_every_key()
    {
        Permissions.Add(Application.InventoryPermissions.CostRecalculate);
        var a = await NewDepot();
        var b = await NewDepot();
        var (item, variants) = await NewItem(Unique("VC"), true, "U");
        await Send(new SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.None, null, null, null, false, null));
        var variant = variants[0];
        var past = MonthStart.AddDays(-5);
        await Post(past.AddDays(-3), Line("LOAD", a, variant, 10, 10m), Line("LOAD", b, variant, 7, 3.33m));
        await Post(past.AddDays(-2), Line("LOAD", a, variant, 5, 11.37m));
        await Post(past.AddDays(-1), Line("UNLOAD", a, variant, -6));
        var (valueA, valueB) = (await Value(a, variant), await Value(b, variant));
        Assert.Equal(2, (await OpenLayers(a, variant)).Count);

        var draft = await Change(item, ValuationMethod.WeightedAverage);
        Assert.Equal(ValuationMethod.Fifo, Assert.Single(draft.Items).OldMethod);
        Assert.Equal(2, draft.Preview.Count);

        Assert.Equal((valueA, valueB), (await Value(a, variant), await Value(b, variant)));
        Assert.Equal(0, (await OpenLayers(a, variant)).Count);
        Assert.Equal(0, await Send(new Application.Movements.RecalculateCost(a, null, variant, null)));

        // O iesire dupa schimbare se costa la medie; o miscare inaintea schimbarii e refuzata.
        var issue = (await Post(Today, Line("UNLOAD", a, variant, -3))).Movements.Single();
        Assert.Equal(-decimal.Round(3 * decimal.Round(valueA / 9, 6), 2, MidpointRounding.AwayFromZero), issue.CostAmount);
        var refused = await Assert.ThrowsAsync<ErpException>(() => Post(past, Line("LOAD", a, variant, 1, 1)));
        Assert.Equal("INV_VALUATION_CHANGE_INVALID", refused.Code);

        // Inapoi la FIFO (in aceeasi luna): stocul devine un singur strat, la valoarea lui.
        var before = await Value(a, variant);
        await Change(item, ValuationMethod.Fifo);
        Assert.Equal(before, await Value(a, variant));
        Assert.Equal((1, 6m, before), await OpenLayers(a, variant));
        Assert.Equal(0, await Send(new Application.Movements.RecalculateCost(a, null, variant, null)));

        await using var connection = new SqlConnection(Database.ConnectionString);
        Assert.Empty(await Dapper.SqlMapper.QueryAsync(connection, "EXEC inv.usp_Stock_Verify @CompanyId = @C", new { C = Database.CompanyId }));
        Assert.Equal(2, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%MetodaEvaluareSchimbata' AND Payload LIKE @I",
            new { I = $"%\"itemIds\":_{item}_%" }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-11")]
    public async Task The_layer_view_shows_the_open_layers_and_their_consumptions()
    {
        var a = await NewDepot();
        var (item, variants) = await NewItem(Unique("VL"), true, "U");
        await Send(new SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.None, null, null, null, false, null));
        await Post(Today.AddDays(-2), Line("LOAD", a, variants[0], 4, 2m));
        await Post(Today.AddDays(-1), Line("LOAD", a, variants[0], 4, 3m));
        await Post(Today, Line("UNLOAD", a, variants[0], -5));

        var view = await Send(new GetCostLayers(variants[0], a));
        var layer = Assert.Single(view.Layers);
        Assert.Equal(("3", "9.00"), (layer.QtyRemaining, layer.ValueRemaining));
        Assert.Equal(["-1", "-4"], view.Consumptions.Select(c => c.Qty).Order());
        Assert.NotNull(await Send(new GetCostAdjustments()));
    }
}
