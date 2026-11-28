using Erp.Core.Contracts;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Straturile de cost pe SQL Server real (02.05, S3 3.1–3.4): FIFO si LIFO prin portul de stoc, transferul care muta straturile cu data lor,
/// stornarea care le reface, valoarea egala cu suma straturilor si a registrului.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CostLayerIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static long nextSource = 500000;

    private async Task<int> NewDepot() =>
        (await Send(new SaveWarehouse(null, Unique("CL-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1,
            null, WarehousePurpose.Regular, null))).Id;

    private async Task<int> Variant(ValuationMethod method)
    {
        var (item, variants) = await NewItem(Unique("CL"), true, "U");
        await Send(new SaveItemStockSettings(item, null, method, TrackingMode.None, null, null, null, false, null));
        return variants[0];
    }

    private static StockMovementRequest Line(string type, int warehouse, int variant, decimal qty, decimal? cost = null, long? source = null, byte role = 1) =>
        new(source ?? Interlocked.Increment(ref nextSource), role, type, null, warehouse, variant, qty, cost, null, null, null, null, null, null, null);

    private Task<StockPostingResult> Post(DateOnly date, long document, params StockMovementRequest[] lines) =>
        InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST", document, "T", date, date,
            null, false, lines), Ct));

    private Task<StockPostingResult> Post(DateOnly date, params StockMovementRequest[] lines) => Post(date, Interlocked.Increment(ref nextSource), lines);

    private Task<(decimal Qty, decimal Value, decimal LayerQty, decimal LayerValue)> State(int warehouse, int variant) =>
        Query<(decimal, decimal, decimal, decimal)>("""
            SELECT ISNULL((SELECT SUM(QtyOnHand) FROM inv.Stock WHERE WarehouseId = @W AND ItemVariantId = @V), 0),
                   ISNULL((SELECT SUM(CostOnHand) FROM inv.Stock WHERE WarehouseId = @W AND ItemVariantId = @V), 0),
                   ISNULL((SELECT SUM(QtyRemaining) FROM inv.CostLayer WHERE WarehouseId = @W AND ItemVariantId = @V), 0),
                   ISNULL((SELECT SUM(ValueRemaining) FROM inv.CostLayer WHERE WarehouseId = @W AND ItemVariantId = @V), 0);
            """, new { W = warehouse, V = variant });

    [Theory]
    [Trait("Criteriu", "02.05-S3-A3")]
    [InlineData(ValuationMethod.Fifo, 1600)]
    [InlineData(ValuationMethod.Lifo, 1700)]
    [InlineData(ValuationMethod.WeightedAverage, 1650)]
    public async Task The_issue_cost_follows_the_item_method(ValuationMethod method, decimal expected)
    {
        var warehouse = await NewDepot();
        var variant = await Variant(method);
        await Post(Today.AddDays(-2), Line("LOAD", warehouse, variant, 100, 10));
        await Post(Today.AddDays(-1), Line("LOAD", warehouse, variant, 100, 12));
        var issue = await Post(Today, Line("UNLOAD", warehouse, variant, -150));
        Assert.Equal(-expected, issue.Movements.Single().CostAmount);
        var state = await State(warehouse, variant);
        Assert.Equal((50m, 2200m - expected), (state.Qty, state.Value));
        if (method != ValuationMethod.WeightedAverage)
        {
            Assert.Equal((state.Qty, state.Value), (state.LayerQty, state.LayerValue));
        }
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A4")]
    public async Task A_transfer_moves_the_layers_with_their_dates_and_its_reversal_puts_them_back()
    {
        var a = await NewDepot();
        var b = await NewDepot();
        var variant = await Variant(ValuationMethod.Fifo);
        await Post(Today.AddDays(-4), Line("LOAD", a, variant, 3, 10));
        await Post(Today.AddDays(-2), Line("LOAD", a, variant, 5, 11));
        var document = Interlocked.Increment(ref nextSource);
        var line = Interlocked.Increment(ref nextSource);
        await Post(Today, document, Line("TRANSFER_OUT", a, variant, -6, source: line), Line("TRANSFER_IN", b, variant, 6, source: line, role: 2));

        Assert.Equal([Today.AddDays(-4), Today.AddDays(-2)], (await QueryDates(b, variant)));
        Assert.Equal((2m, 22m), ((await State(a, variant)).Qty, (await State(a, variant)).Value));
        Assert.Equal((6m, 63m, 6m, 63m), await State(b, variant));

        await InTransaction(sp => sp.GetRequiredService<IStockLedger>().ReverseAsync(new StockReversalRequest(Database.CompanyId, "test", document, null, Today,
            "TEST", document + 1, "TS"), Ct));
        Assert.Equal((8m, 85m, 8m, 85m), await State(a, variant));
        Assert.Equal((0m, 0m, 0m, 0m), await State(b, variant));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-3.2")]
    public async Task A_receipt_whose_layer_was_consumed_cannot_be_reversed()
    {
        var warehouse = await NewDepot();
        var variant = await Variant(ValuationMethod.Fifo);
        var document = Interlocked.Increment(ref nextSource);
        await Post(Today, document, Line("LOAD", warehouse, variant, 5, 2));
        await Post(Today, Line("UNLOAD", warehouse, variant, -1));
        var refused = await Assert.ThrowsAsync<ErpException>(() => InTransaction(sp => sp.GetRequiredService<IStockLedger>().ReverseAsync(
            new StockReversalRequest(Database.CompanyId, "test", document, null, Today, "TEST", document + 1, "TS"), Ct)));
        Assert.Equal("INV_COST_REVERSAL_LAYER_CONSUMED", refused.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A1")]
    [Trait("Criteriu", "02.05-S3-A2")]
    public async Task Random_fifo_and_lifo_sequences_keep_layers_stock_and_ledger_equal()
    {
        var warehouse = await NewDepot();
        var random = new Random(28112026);
        foreach (var method in new[] { ValuationMethod.Fifo, ValuationMethod.Lifo })
        {
            var variant = await Variant(method);
            decimal qty = 0;
            for (var i = 0; i < 30; i++)
            {
                if (qty <= 0 || random.Next(3) == 0)
                {
                    var q = random.Next(1, 30);
                    await Post(Today, Line("LOAD", warehouse, variant, q, random.Next(100, 999) / 37m));
                    qty += q;
                }
                else
                {
                    var q = random.Next(1, (int)qty + 1);
                    await Post(Today, Line("UNLOAD", warehouse, variant, -q));
                    qty -= q;
                }

                var state = await State(warehouse, variant);
                Assert.Equal((state.Qty, state.Value), (state.LayerQty, state.LayerValue));
                if (state.Qty == 0)
                {
                    Assert.Equal(0m, state.Value);
                }
            }
        }

        await using var connection = new SqlConnection(Database.ConnectionString);
        Assert.Empty(await Dapper.SqlMapper.QueryAsync(connection, "EXEC inv.usp_Stock_Verify @CompanyId = @C, @WarehouseId = @W",
            new { C = Database.CompanyId, W = warehouse }));
    }

    private async Task<IReadOnlyList<DateOnly>> QueryDates(int warehouse, int variant)
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        return (await Dapper.SqlMapper.QueryAsync<DateTime>(connection,
            "SELECT LayerDate FROM inv.CostLayer WHERE WarehouseId = @W AND ItemVariantId = @V AND QtyRemaining > 0 ORDER BY LayerDate",
            new { W = warehouse, V = variant })).Select(DateOnly.FromDateTime).ToList();
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A5")]
    public async Task A_backdated_receipt_recalculates_the_later_issues_and_the_adjustments_explain_the_value_change()
    {
        var warehouse = await NewDepot();
        var variant = await Variant(ValuationMethod.WeightedAverage);
        await Post(Today.AddDays(-10), Line("LOAD", warehouse, variant, 10, 10));
        var issue = (await Post(Today.AddDays(-5), Line("UNLOAD", warehouse, variant, -5))).Movements.Single();
        Assert.Equal(-50m, issue.CostAmount);

        await Post(Today.AddDays(-8), Line("LOAD", warehouse, variant, 10, 4));

        Assert.Equal(-35m, await Query<decimal>("SELECT CostAmount FROM inv.StockMovement WHERE Id = @Id", new { Id = issue.MovementId }));
        Assert.Equal(15m, await Query<decimal>("SELECT SUM(Amount) FROM inv.CostAdjustment WHERE MovementId = @Id AND Kind = 1", new { Id = issue.MovementId }));
        Assert.Equal((15m, 105m), ((await State(warehouse, variant)).Qty, (await State(warehouse, variant)).Value));
        Assert.Equal(0, await Query<int>("SELECT COUNT(*) FROM inv.CostRecalcQueue WHERE WarehouseId = @W", new { W = warehouse }));
        Assert.True(await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%CostStocAjustat%' AND Payload LIKE @M",
            new { M = $"%\"sourceMovementId\":{issue.MovementId},%" }) == 1);

        // A doua recalculare nu mai schimba nimic.
        Permissions.Add(Application.InventoryPermissions.CostRecalculate);
        Assert.Equal(0, await Send(new Application.Movements.RecalculateCost(warehouse, null, variant, null)));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-A5")]
    [Trait("Criteriu", "02.05-S3-A4")]
    public async Task A_recalculated_transfer_issue_recalculates_the_receiving_warehouse_too()
    {
        var a = await NewDepot();
        var b = await NewDepot();
        var variant = await Variant(ValuationMethod.Lifo);
        await Post(Today.AddDays(-10), Line("LOAD", a, variant, 10, 10));
        var line = Interlocked.Increment(ref nextSource);
        await Post(Today.AddDays(-5), Interlocked.Increment(ref nextSource), Line("TRANSFER_OUT", a, variant, -4, source: line),
            Line("TRANSFER_IN", b, variant, 4, source: line, role: 2));
        Assert.Equal((4m, 40m), ((await State(b, variant)).Qty, (await State(b, variant)).Value));

        // LIFO: recepția din ziua -7 e cea mai nouă la data transferului, deci transferul ia de acolo, la 6,00.
        await Post(Today.AddDays(-7), Line("LOAD", a, variant, 10, 6));

        Assert.Equal((4m, 24m, 4m, 24m), await State(b, variant));
        Assert.Equal((16m, 136m, 16m, 136m), await State(a, variant));
        await using var connection = new SqlConnection(Database.ConnectionString);
        Assert.Empty(await Dapper.SqlMapper.QueryAsync(connection, "EXEC inv.usp_Stock_Verify @CompanyId = @C", new { C = Database.CompanyId }));
    }
}
