using Erp.Core.Contracts;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Inventory.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>Bonurile POS sincronizate (02.05, S2 4.7) si registrul de miscari cu soldul dupa fiecare miscare (S2 10; macheta s2-05).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PosAndRegisterIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);

    private async Task<int> NewStore() =>
        (await Send(new SaveWarehouse(null, Unique("MAG-"), "Magazin", Database.ClujStoreId, WarehouseKind.Store, ValuationBasis.Cost, null, null, null, null, 1,
            null, WarehousePurpose.Regular, null))).Id;

    private Task Sync(BonuriPOSSincronizate evt) =>
        InTransaction(sp => new PostPosReceipts(sp.GetRequiredService<IStockLedger>(), sp.GetRequiredService<IPeriodLock>()).HandleAsync(evt, Ct));

    [Fact]
    [Trait("Criteriu", "02.05-S2-A9")]
    public async Task Synced_receipts_on_zero_stock_are_forced_negative_and_a_replay_does_not_double_them()
    {
        var store = await NewStore();
        var (_, variants) = await NewItem(Unique("POS"), true, "U");
        var evt = new BonuriPOSSincronizate(7, [
            new PosReceipt(900001, "BF-1", Today.AddDays(-20), store, null, [new PosReceiptLine(9000011, variants[0], 3)]),
            new PosReceipt(900002, "BF-2", Today.AddDays(-19), store, null, [new PosReceiptLine(9000021, variants[0], -1)]),
        ]) { CompanyId = Database.CompanyId };

        await Sync(evt);
        await Sync(evt);

        Assert.Equal(-2m, await Query<decimal>("SELECT QtyOnHand FROM inv.Stock WHERE WarehouseId = @W AND ItemVariantId = @V", new { W = store, V = variants[0] }));
        Assert.Equal(2, await Query<int>("SELECT COUNT(*) FROM inv.StockMovement WHERE SourceModule = 'pos' AND WarehouseId = @W", new { W = store }));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM inv.StockMovement WHERE SourceModule = 'pos' AND WarehouseId = @W AND IsForcedNegative = 1",
            new { W = store }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-10")]
    public async Task The_register_shows_the_movements_in_posting_order_with_the_balance_after_each_one()
    {
        var store = await NewStore();
        var (_, variants) = await NewItem(Unique("REG"), true, "U");
        foreach (var (date, qty, type) in new[] { (Today.AddDays(-3), 10m, "LOAD"), (Today.AddDays(-2), -4m, "UNLOAD"), (Today.AddDays(-1), 5m, "LOAD") })
        {
            await InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
                Random.Shared.NextInt64(1, long.MaxValue / 2), "T", date, date, null, false,
                [new StockMovementRequest(Random.Shared.NextInt64(1, long.MaxValue / 2), 1, type, null, store, variants[0], qty, qty > 0 ? 2 : null, null, null, null,
                    null, null, null, null)]), Ct));
        }

        var page = await Send(new GetStockMovements(store, variants[0]));
        Assert.Equal(["10", "6", "11"], page.Select(m => m.BalanceQty));
        var next = await Send(new GetStockMovements(store, variants[0], AfterSeq: page[0].PostingSeq, Top: 1));
        Assert.Equal(page[1].Id, Assert.Single(next).Id);
    }
}
