using Erp.Core.Contracts;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Lots;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Starea si termenul loturilor pe SQL Server real (02.05, S4 3.2, 3.3, 3.5): jobul de expirare, carantina si blocarea cu istoric, durata minima
/// a clientului, motivul alegerii altui lot, propunerea si cautarea prin portul de stoc.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LotStatusIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static long nextSource = 1_700_000;

    private async Task<int> NewDepot() =>
        (await Send(new SaveWarehouse(null, Unique("LS-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1,
            null, WarehousePurpose.Regular, null))).Id;

    private async Task<(int Item, int Variant)> TrackedItem()
    {
        var (item, ids) = await NewItem(Unique("LS"), true, "U");
        await Send(new SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.LotAndExpiry, null, null, null, false, null));
        return (item, ids[0]);
    }

    private static StockMovementRequest In(int warehouse, int variant, decimal qty, string lot, DateOnly expiry) =>
        new(Interlocked.Increment(ref nextSource), 1, "LOAD", null, warehouse, variant, qty, 1m, null, lot, expiry, null, null, null, null);

    private static StockMovementRequest Sale(int warehouse, int variant, decimal qty, int? lot = null, byte? reason = null) =>
        new(Interlocked.Increment(ref nextSource), 1, "ISSUE_SALE", null, warehouse, variant, -qty, null, lot, null, null, null, null, null, null)
        {
            LotOverrideReason = reason,
        };

    private Task<StockPostingResult> Post(int? partner, params StockMovementRequest[] lines) =>
        InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            Interlocked.Increment(ref nextSource), "T", Today, Today, partner, false, lines), Ct));

    private Task<int> LotId(int variant, string code) =>
        Query<int>("SELECT Id FROM inv.Lot WHERE ItemVariantId = @V AND LotCode = @C", new { V = variant, C = code });

    [Fact]
    [Trait("Criteriu", "02.05-S4-A4")]
    public async Task The_daily_job_expires_only_use_by_lots_and_warns_about_the_ones_close_to_expiry()
    {
        var warehouse = await NewDepot();
        var (useByItem, useBy) = await TrackedItem();
        var (bestBeforeItem, bestBefore) = await TrackedItem();
        await Execute("UPDATE inv.ItemStockSettings SET ExpiryKind = 2 WHERE ItemId = @I", new { I = bestBeforeItem });
        await Post(null, In(warehouse, useBy, 5, "U1", Today.AddDays(10)), In(warehouse, bestBefore, 5, "B1", Today.AddDays(10)),
            In(warehouse, useBy, 5, "U2", Today.AddDays(12)));
        await Execute("UPDATE inv.Lot SET ExpiryDate = @D WHERE LotCode IN (N'U1', N'B1') AND ItemVariantId IN (@A, @B)",
            new { D = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue), A = useBy, B = bestBefore });

        Assert.True(await Send(new ScanLotExpiry(Today)) >= 1);

        Assert.Equal(LotStatus.Expired, (await Send(new GetLot(await LotId(useBy, "U1")))).Status);
        var kept = await Send(new GetLot(await LotId(bestBefore, "B1")));
        Assert.Equal(LotStatus.Available, kept.Status);
        Assert.Contains((await Send(new GetLot(await LotId(useBy, "U1")))).History, h => h.ToStatus == LotStatus.Expired);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%LotStareSchimbata' AND Payload LIKE @L",
            new { L = $"%\"lotId\":{await LotId(useBy, "U1")},%" }));
        Assert.True(await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%LoturiAproapeDeExpirare' AND Payload LIKE @W",
            new { W = $"%\"warehouseId\":{warehouse},%" }) >= 1);
        var expired = await Send(new GetLot(await LotId(useBy, "U1")));
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new ChangeLotStatus(expired.LotId, LotStatus.Available, null, expired.RowVersion)));
        Assert.Equal("INV_LOT_STATUS_TRANSITION_INVALID", refused.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A3")]
    public async Task Quarantine_and_block_keep_the_lot_out_of_sales_until_it_is_released()
    {
        var warehouse = await NewDepot();
        var (item, variant) = await TrackedItem();
        await Execute("UPDATE inv.ItemStockSettings SET ReceiptQuarantine = 1 WHERE ItemId = @I", new { I = item });
        await Post(null, In(warehouse, variant, 10, "Q1", Today.AddDays(60)));
        var lot = await Send(new GetLot(await LotId(variant, "Q1")));
        Assert.Equal(LotStatus.Quarantine, lot.Status);
        Assert.Empty(await WithService<IStockLedger, IReadOnlyList<LotAllocationDto>>(s => s.ProposeLotsAsync(new LotProposalRequest(Database.CompanyId, warehouse, null,
            variant, 5, null, Today, null), Ct)));

        var released = await Send(new ChangeLotStatus(lot.LotId, LotStatus.Available, "Buletin de analiză conform", lot.RowVersion));
        await Post(null, Sale(warehouse, variant, 2));
        await Send(new ChangeLotStatus(lot.LotId, LotStatus.Blocked, "Reclamație client", released.RowVersion));
        var blocked = await Assert.ThrowsAsync<ErpException>(() => Post(null, Sale(warehouse, variant, 1)));
        Assert.Equal("INV_LOT_INSUFFICIENT_QTY", blocked.Code);
        Assert.Equal(3, (await Send(new GetLot(lot.LotId))).History.Count);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A2")]
    [Trait("Criteriu", "02.05-S4-3.2")]
    public async Task The_customer_minimum_shelf_life_drives_the_proposal_and_another_lot_needs_a_reason()
    {
        var warehouse = await NewDepot();
        var (item, variant) = await TrackedItem();
        await Post(null, In(warehouse, variant, 480, "L2609-11", Today.AddDays(9)), In(warehouse, variant, 2400, "L2610-18", Today.AddDays(45)),
            In(warehouse, variant, 960, "L2611-07", Today.AddDays(49)));
        await Send(new SavePartnerShelfLifeRule(Database.LogisticPartnerId, item, null, 45));

        var proposal = await WithService<IStockLedger, IReadOnlyList<LotAllocationDto>>(s => s.ProposeLotsAsync(new LotProposalRequest(Database.CompanyId, warehouse,
            null, variant, 3000, Database.LogisticPartnerId, Today, null), Ct));
        Assert.Equal([("L2610-18", 2400m, true), ("L2611-07", 600m, false)], proposal.Select(p => (p.LotCode, p.QtyBase, p.IsFirstChoice)));

        var shortLot = await LotId(variant, "L2609-11");
        var tooShort = await Assert.ThrowsAsync<ErpException>(() => Post(Database.LogisticPartnerId, Sale(warehouse, variant, 10, shortLot, 1)));
        Assert.Equal("INV_LOT_SHELF_LIFE_TOO_SHORT", tooShort.Code);
        var noReason = await Assert.ThrowsAsync<ErpException>(() => Post(Database.LogisticPartnerId, Sale(warehouse, variant, 10, proposal[1].LotId)));
        Assert.Equal("INV_LOT_OVERRIDE_REASON_REQUIRED", noReason.Code);
        var chosen = await Post(Database.LogisticPartnerId, Sale(warehouse, variant, 10, proposal[1].LotId, 1));
        Assert.Equal((byte)1, await Query<byte>("SELECT LotOverrideReason FROM inv.StockMovement WHERE Id = @Id", new { Id = chosen.Movements.Single().MovementId }));

        // Fara client, lotul scurt se poate descarca (casare); cautarea dupa CKU gaseste lotul.
        var cku = await Send(new GetLot(proposal[0].LotId));
        var found = await WithService<IStockLedger, LotInfoDto?>(s => s.FindCkuAsync(Database.CompanyId, cku.CkuUid.ToString(), Ct));
        Assert.Equal(cku.CkuCode, found?.CkuCode);
        Assert.Equal(cku.LotId, (await WithService<IStockLedger, LotInfoDto?>(s => s.FindLotAsync(Database.CompanyId, variant, "L2610-18", null, Ct)))?.LotId);
    }
}
