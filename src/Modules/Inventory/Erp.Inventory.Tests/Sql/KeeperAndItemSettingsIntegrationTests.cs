using Erp.Core.Contracts;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Infrastructure.Events;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Gestionarii, predarea-primirea, setarile de stoc ale articolului si politica efectiva pe SQL Server real (02.05, S1 3.2, 3.4, 3.6, 4.2).
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class KeeperAndItemSettingsIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private async Task<int> NewDepot(string prefix = "DG-")
    {
        var saved = await Send(new SaveWarehouse(null, Unique(prefix), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null,
            null, null, null, 1, null, WarehousePurpose.Regular, null));
        return saved.Id;
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A6")]
    public async Task Overlapping_holders_are_refused_by_the_database()
    {
        var warehouse = await NewDepot();
        var gheorghe = await NewEmployee("Gheorghe", "Ilie");
        var ana = await NewEmployee("Ana", "Pop");
        await Send(new SaveWarehouseKeeper(null, warehouse, gheorghe, 1, new DateOnly(2026, 1, 1), null, "Decizia 12/2026", null));
        await Send(new SaveWarehouseKeeper(null, warehouse, ana, 2, new DateOnly(2026, 1, 1), null, null, null));

        var overlap = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveWarehouseKeeper(null, warehouse, ana, 1, new DateOnly(2026, 9, 1), null,
            null, null)));
        Assert.Equal("INV_KEEPER_OVERLAP", overlap.Code);

        var other = await NewEmployee("Ion", "Străin", Database.OtherCompanyId);
        var foreign = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveWarehouseKeeper(null, warehouse, other, 2, new DateOnly(2026, 1, 1),
            null, null, null)));
        Assert.Equal("INV_INVALID_REFERENCE", foreign.Code);

        var list = await Send(new GetWarehouseKeepers(warehouse));
        Assert.Equal(["Gheorghe Ilie", "Ana Pop"], list.Keepers.Select(k => k.EmployeeName));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A7")]
    public async Task A_handover_with_a_required_count_waits_for_it_then_keeps_the_periods_continuous_and_unblocks()
    {
        var warehouse = await NewDepot("PG-");
        var from = await NewEmployee("Gheorghe", "Ilie");
        var to = await NewEmployee("Mihai", "Stan");
        await Send(new SaveWarehouseKeeper(null, warehouse, from, 1, new DateOnly(2026, 1, 1), null, null, null));

        var started = await Send(new StartHandover(warehouse, new DateOnly(2026, 11, 15), to, "Predare la plecare"));
        Assert.True((await Send(new GetWarehouse(warehouse))).IsBlocked);
        var second = await Assert.ThrowsAsync<ErpException>(() => Send(new StartHandover(warehouse, new DateOnly(2026, 11, 16), to, null)));
        Assert.Equal("INV_HANDOVER_IN_PROGRESS", second.Code);

        var handover = Assert.Single((await Send(new GetWarehouseKeepers(warehouse))).Handovers);
        Assert.Equal((true, (byte)2, "Gheorghe Ilie"), (handover.RequiresCount, handover.Status, handover.FromEmployeeName));
        var noCount = await Assert.ThrowsAsync<ErpException>(() => Send(new CompleteHandover(started.Id, started.RowVersion)));
        Assert.Equal("INV_HANDOVER_COUNT_REQUIRED", noCount.Code);

        // Inventarul de predare (S6) e simulat: legarea lui permite finalizarea.
        await Execute("UPDATE inv.WarehouseHandover SET InventoryCountId = 1 WHERE Id = @Id", new { started.Id });
        var rowVersion = (await Send(new GetWarehouseKeepers(warehouse))).Handovers.Single().RowVersion;
        await Send(new CompleteHandover(started.Id, rowVersion));

        var after = await Send(new GetWarehouseKeepers(warehouse));
        var holders = after.Keepers.Where(k => k.Role == 1).OrderBy(k => k.ValidFrom).ToList();
        Assert.Equal((new DateOnly(2026, 11, 14), (DateOnly?)null), (holders[0].ValidTo!.Value, holders[1].ValidTo));
        Assert.Equal((to, new DateOnly(2026, 11, 15), started.Id), (holders[1].EmployeeId, holders[1].ValidFrom, holders[1].HandoverId!.Value));
        Assert.False((await Send(new GetWarehouse(warehouse))).IsBlocked);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%GestionarSchimbat%' AND Payload LIKE @W",
            new { W = $"%\"warehouseId\":{warehouse},%" }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-4.2")]
    public async Task A_cancelled_handover_leaves_the_keepers_and_lifts_the_block()
    {
        var warehouse = await NewDepot("PC-");
        var to = await NewEmployee("Mihai", "Stan");
        var started = await Send(new StartHandover(warehouse, new DateOnly(2026, 11, 20), to, null));
        await Send(new CancelHandover(started.Id, started.RowVersion));
        var after = await Send(new GetWarehouseKeepers(warehouse));
        Assert.Equal((byte)4, after.Handovers.Single().Status);
        Assert.Empty(after.Keepers);
        Assert.False((await Send(new GetWarehouse(warehouse))).IsBlocked);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A15")]
    public async Task Item_settings_need_tracking_for_specific_identification_and_keep_the_method()
    {
        var (item, _) = await NewItem(Unique("APA"), true, "05L");
        var (service, _) = await NewItem(Unique("SRV"), false);

        var defaults = await Send(new GetItemStockSettings(item));
        Assert.Equal((null, ValuationMethod.WeightedAverage, TrackingMode.None), (defaults.Company.Id, defaults.Company.ValuationMethod,
            defaults.Company.TrackingMode));

        var specific = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveItemStockSettings(item, null, ValuationMethod.SpecificIdentification,
            TrackingMode.None, null, null, null, false, null)));
        Assert.Equal("INV_SPECIFIC_ID_REQUIRES_TRACKING", specific.Code);
        var notStockable = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveItemStockSettings(service, null, ValuationMethod.Fifo,
            TrackingMode.None, null, null, null, false, null)));
        Assert.Equal("INV_INVALID_REFERENCE", notStockable.Code);

        var saved = await Send(new SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.LotAndExpiry, 10, 100, null, false, null));
        var tab = await Send(new GetItemStockSettings(item));
        Assert.Equal((ValuationMethod.Fifo, TrackingMode.LotAndExpiry, ReplenishmentMethod.MinMax), (tab.Company.ValuationMethod, tab.Company.TrackingMode,
            tab.Company.ReplenishmentMethod));
        var method = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveItemStockSettings(item, null, ValuationMethod.WeightedAverage,
            TrackingMode.LotAndExpiry, 10, 100, null, false, saved.RowVersion)));
        Assert.Equal("INV_VALUATION_CHANGE_INVALID", method.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A8")]
    public async Task The_policy_provider_applies_the_precedence_from_the_database_rows()
    {
        var warehouse = await NewDepot("PP-");
        var (item, variants) = await NewItem(Unique("DZ"), true, "ALB", "NEGRU");
        await Send(new SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.None, 5, 50, null, true, null));
        await Send(new SaveItemWarehouseSettings(null, warehouse, item, null, 2, 20, 10, null, false, false, true, null));
        await Send(new SaveItemWarehouseSettings(null, warehouse, item, variants[0], 1, 8, 4, ReplenishmentMethod.Mrp, false, true, true, null));
        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveItemWarehouseSettings(null, warehouse, item, variants[0], 1, 8, 4,
            null, false, false, true, null)));
        Assert.Equal("INV_ITEM_WAREHOUSE_SETTINGS_EXISTS", duplicate.Code);

        var policies = await WithService<IStockPolicyProvider, IReadOnlyDictionary<int, ItemStockPolicyDto>>(p =>
            p.GetManyAsync(warehouse, variants, Ct));
        var white = policies[variants[0]];
        var black = policies[variants[1]];
        Assert.Equal((1m, 8m, 4m, true, ReplenishmentMethod.Mrp), (white.MinQty!.Value, white.MaxQty!.Value, white.ReorderQty!.Value, white.IsBlockedForReceipt,
            white.Replenishment));
        Assert.Equal((2m, 20m, false, ReplenishmentMethod.MinMax), (black.MinQty!.Value, black.MaxQty!.Value, black.IsBlockedForReceipt, black.Replenishment));
        // Blocarea iesirilor pe tot modelul (firma) se aplica in ambele variante; metoda e a modelului.
        Assert.True(white.IsBlockedForIssue && black.IsBlockedForIssue);
        Assert.Equal(ValuationMethod.Fifo, black.Method);

        var tab = await Send(new GetItemStockSettings(item));
        var row = tab.Warehouses.Single(w => w.ItemVariantId is null);
        await Send(new DeleteItemWarehouseSettings(row.Id, row.RowVersion));
        var afterDelete = await WithService<IStockPolicyProvider, ItemStockPolicyDto>(p => p.GetAsync(warehouse, variants[1], Ct));
        Assert.Equal((5m, 50m), (afterDelete.MinQty!.Value, afterDelete.MaxQty!.Value));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-5.2")]
    public async Task A_new_stockable_item_gets_the_company_defaults_once()
    {
        await NewDepot();
        var (item, _) = await NewItem(Unique("NOU"), true);
        var evt = new ArticolModificat(item, "NOU", "Articol nou", 1, "H87", "S", null, true);
        await InTransaction(sp => new EnsureItemStockSettings((IItemStockSettingsStore)sp.GetService(typeof(IItemStockSettingsStore))!,
            (ICurrentContext)sp.GetService(typeof(ICurrentContext))!).HandleAsync(evt, Ct));
        await InTransaction(sp => new EnsureItemStockSettings((IItemStockSettingsStore)sp.GetService(typeof(IItemStockSettingsStore))!,
            (ICurrentContext)sp.GetService(typeof(ICurrentContext))!).HandleAsync(evt, Ct));
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM inv.ItemStockSettings WHERE ItemId = @Item AND CompanyId = @Company",
            new { Item = item, Company = Database.CompanyId }));
    }
}
