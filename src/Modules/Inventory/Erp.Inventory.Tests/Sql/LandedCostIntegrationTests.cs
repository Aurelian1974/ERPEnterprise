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
/// Costurile adaugate pe SQL Server real (02.05, S3 3.3, 4.2): documentul CA, impartirea intre stoc si iesirile deja facute (decizia 3), lantul
/// transferurilor, portul folosit de Achizitii, valoarea stocului egala cu registrul si ajustarile.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LandedCostIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static long nextSource = 700000;

    private async Task<int> NewDepot() =>
        (await Send(new SaveWarehouse(null, Unique("LC-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1,
            null, WarehousePurpose.Regular, null))).Id;

    private async Task<int> Variant(ValuationMethod method)
    {
        var (item, variants) = await NewItem(Unique("LC"), true, "U");
        await Send(new SaveItemStockSettings(item, null, method, TrackingMode.None, null, null, null, false, null));
        return variants[0];
    }

    private static StockMovementRequest Line(string type, int warehouse, int variant, decimal qty, decimal? cost = null, long? source = null, byte role = 1) =>
        new(source ?? Interlocked.Increment(ref nextSource), role, type, null, warehouse, variant, qty, cost, null, null, null, null, null, null, null);

    private async Task<PostedMovementDto> Post(DateOnly date, params StockMovementRequest[] lines) =>
        (await InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            Interlocked.Increment(ref nextSource), "T", date, date, null, false, lines), Ct))).Movements[0];

    private Task<decimal> Value(int warehouse, int variant) =>
        Query<decimal>("SELECT ISNULL(SUM(CostOnHand), 0) FROM inv.Stock WHERE WarehouseId = @W AND ItemVariantId = @V", new { W = warehouse, V = variant });

    private Task<decimal> Cost(long movement) => Query<decimal>("SELECT CostAmount FROM inv.StockMovement WHERE Id = @Id", new { Id = movement });

    private async Task<LandedCostDto> CreateAndValidate(decimal amount, byte basis, params long[] movements)
    {
        var saved = await Send(new SaveLandedCost(null, Today, null, 1, 9001, null, amount.ToString(System.Globalization.CultureInfo.InvariantCulture), basis, null,
            movements.Select(m => new LandedCostLineInput(m, null)).ToList(), null));
        await Send(new ValidateLandedCost(saved.Id, saved.RowVersion));
        return await Send(new GetLandedCost(saved.Id));
    }

    private async Task AssertLedgerConsistent()
    {
        await using var connection = new SqlConnection(Database.ConnectionString);
        Assert.Empty(await Dapper.SqlMapper.QueryAsync(connection, "EXEC inv.usp_Stock_Verify @CompanyId = @C", new { C = Database.CompanyId }));
    }

    [Theory]
    [Trait("Criteriu", "02.05-S3-A6")]
    [InlineData(ValuationMethod.Fifo)]
    [InlineData(ValuationMethod.Lifo)]
    [InlineData(ValuationMethod.WeightedAverage)]
    public async Task An_added_cost_on_a_receipt_sold_40_percent_goes_60_percent_to_stock_and_40_percent_to_the_issue(ValuationMethod method)
    {
        var warehouse = await NewDepot();
        var variant = await Variant(method);
        var receipt = await Post(Today.AddDays(-3), Line("RECEIPT_PURCHASE", warehouse, variant, 100, 50));
        var issue = await Post(Today.AddDays(-1), Line("ISSUE_SALE", warehouse, variant, -40));
        Assert.Equal(-2000m, issue.CostAmount);

        var document = await CreateAndValidate(1840m, 1, receipt.MovementId);

        Assert.Equal((byte)2, document.Status);
        Assert.NotNull(document.Number);
        Assert.Equal(("1104.00", "736.00"), (document.StockAmount, document.ExpenseAmount));
        Assert.Equal(6840m, await Cost(receipt.MovementId));
        Assert.Equal(-2736m, await Cost(issue.MovementId));
        Assert.Equal(4104m, await Value(warehouse, variant));
        Assert.Equal(1840m, await Query<decimal>("""
            SELECT SUM(Amount) FROM inv.CostAdjustment WHERE SourceDocumentType = 'CA' AND SourceDocumentId = @Id AND Kind = 3 AND MovementId = @M
            """, new { document.Id, M = receipt.MovementId }));
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%CostStocAjustat%' AND Payload LIKE @Doc AND Payload LIKE N'%"targetKind":"Expense"%'
            """, new { Doc = $"%\"sourceDocumentType\":\"CA\",\"sourceDocumentId\":{document.Id},%" }));
        await AssertLedgerConsistent();

        // O recalculare ulterioara nu mai schimba nimic: costul adaugat e acum in valoarea receptiei.
        Permissions.Add(Application.InventoryPermissions.CostRecalculate);
        Assert.Equal(0, await Send(new Application.Movements.RecalculateCost(warehouse, null, variant, null)));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-4.5")]
    public async Task An_added_cost_follows_the_goods_through_a_transfer()
    {
        var a = await NewDepot();
        var b = await NewDepot();
        var variant = await Variant(ValuationMethod.Fifo);
        var receipt = await Post(Today.AddDays(-4), Line("RECEIPT_PURCHASE", a, variant, 10, 10));
        var line = Interlocked.Increment(ref nextSource);
        await Post(Today.AddDays(-3), Line("TRANSFER_OUT", a, variant, -4, source: line), Line("TRANSFER_IN", b, variant, 4, source: line, role: 2));
        await Post(Today.AddDays(-2), Line("ISSUE_SALE", b, variant, -1));

        var document = await CreateAndValidate(100m, 2, receipt.MovementId);

        Assert.Equal(("90.00", "10.00"), (document.StockAmount, document.ExpenseAmount));
        Assert.Equal(120m, await Value(a, variant));
        Assert.Equal(60m, await Value(b, variant));
        await AssertLedgerConsistent();

        var transferIn = await Query<long>("SELECT Id FROM inv.StockMovement WHERE WarehouseId = @W AND ItemVariantId = @V AND QtyBase > 0", new { W = b, V = variant });
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveLandedCost(null, Today, null, 1, null, null, "5.00", 1, null,
            [new LandedCostLineInput(transferIn, null)], null)));
        Assert.Equal("INV_LANDED_COST_TARGET_INVALID", refused.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-4.2")]
    public async Task Purchasing_creates_the_document_through_the_port_by_receipt_lines()
    {
        var warehouse = await NewDepot();
        var variant = await Variant(ValuationMethod.Fifo);
        var first = Line("RECEIPT_PURCHASE", warehouse, variant, 30, 10);
        var second = Line("RECEIPT_PURCHASE", warehouse, variant, 10, 10);
        await Post(Today.AddDays(-2), first, second);

        var noWeight = await Assert.ThrowsAsync<ErpException>(() => InTransaction(sp => sp.GetRequiredService<IStockLedger>().AdjustInboundCostAsync(
            new InboundCostAdjustmentRequest(Database.CompanyId, InboundCostSourceKind.ServiceInvoice, 77, Today, 200m, AllocationBasis.Weight,
                [new InboundCostTarget("test", first.SourceLineId, null)], false), Ct)));
        Assert.Equal(Erp.Inventory.Domain.Costing.LandedCostAllocator.BasisMissing, noWeight.Code);

        var id = await InTransaction(sp => sp.GetRequiredService<IStockLedger>().AdjustInboundCostAsync(new InboundCostAdjustmentRequest(Database.CompanyId,
            InboundCostSourceKind.ServiceInvoice, 77, Today, 200m, AllocationBasis.Quantity,
            [new InboundCostTarget("test", first.SourceLineId, null), new InboundCostTarget("test", second.SourceLineId, null)], true), Ct));

        var document = await Send(new GetLandedCost(id));
        Assert.Equal((byte)2, document.Status);
        Assert.Equal(["150.00", "50.00"], document.Lines.Select(l => l.AllocatedAmount));
        Assert.Equal(600m, await Value(warehouse, variant));
        await AssertLedgerConsistent();
    }
}
