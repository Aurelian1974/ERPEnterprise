using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Depozitele, nivelul de evidenta si regula de propunere a gestiunii pe SQL Server real (02.05, S1 3.8), cu datele demo: locatia Depozit
/// central Ploiesti tine stocul pe depozit, cu halele HALA-A si HALA-B; DC-PL e in ambele, AMB-PL doar in HALA-A.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DepotIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private async Task<int> NewLocation(string code, byte type = 2) =>
        await Query<int>("""
            INSERT INTO core.Location (CompanyId, Code, Name, LocationType, CreatedBy) VALUES (@CompanyId, @Code, @Code, @Type, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Database.CompanyId, Code = code, Type = type });

    private Task<InventorySaved> Warehouse(string code, int? locationId) =>
        Send(new SaveWarehouse(null, code, "Gestiune " + code, locationId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1, null,
            WarehousePurpose.Regular, null));

    [Fact]
    [Trait("Criteriu", "02.05-S1-3.8")]
    public async Task A_depot_level_location_holds_depots_with_shared_warehouses_and_proposes_the_line_warehouse()
    {
        var ploiesti = await NewLocation(Unique("PL"));
        await Send(new SetStockLevel(null, ploiesti, StockKeepingLevel.Depot));
        var page = await Send(new GetDepots());
        Assert.Equal((StockKeepingLevel.Depot, (byte)3), page.Locations.Where(l => l.Id == ploiesti).Select(l => (l.Level, l.Source)).Single());
        Assert.Equal(StockKeepingLevel.Depot, await WithService<IWarehouseLookup, StockKeepingLevel>(l => l.GetLevelAsync(ploiesti, Ct)));

        // La nivelul „depozit” gestiunea nu se leaga direct de locatie.
        var direct = await Assert.ThrowsAsync<ErpException>(() => Warehouse(Unique("X"), ploiesti));
        Assert.Equal("INV_DEPOT_LOCATION_INVALID", direct.Code);
        var marfa = await Warehouse(Unique("DC-PL"), null);
        var ambalaje = await Warehouse(Unique("AMB-PL"), null);

        var halaA = await Send(new SaveDepot(null, Unique("HALA-A"), "Hala A", ploiesti, null, true, null));
        var halaB = await Send(new SaveDepot(null, Unique("HALA-B"), "Hala B", ploiesti, null, true, null));
        await Send(new SetDepotWarehouse(halaA.Id, marfa.Id, true));
        await Send(new SetDepotWarehouse(halaA.Id, ambalaje.Id, true));
        await Send(new SetDepotWarehouse(halaB.Id, marfa.Id, true));
        var hallA = (await Send(new GetDepots())).Depots.Single(d => d.Id == halaA.Id);
        await Send(new SaveDepot(halaA.Id, hallA.Code, hallA.Name, ploiesti, marfa.Id, true, hallA.RowVersion));
        Assert.Equal("DEPOT", await Query<string>("SELECT v.DimensionType FROM inv.Depot AS d JOIN core.DimensionValue AS v ON v.Id = d.DimensionValueId WHERE d.Id = @Id",
            new { halaA.Id }));

        // Grupa Ambalaje merge in AMB-PL, restul in gestiunea implicita a depozitului.
        var group = await Query<int>("""
            INSERT INTO core.ItemGroup (Code, Name, CreatedBy) VALUES (@Code, N'Ambalaje', 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { Code = Unique("AMB") });
        var (box, boxVariants) = await NewItem(Unique("CUTIE"), true, "MARE");
        await Execute("UPDATE core.Item SET ItemGroupId = @Group WHERE Id = @Id", new { Group = group, Id = box });
        var (_, waterVariants) = await NewItem(Unique("APA"), true, "05L");
        await Send(new SaveDepotRule(halaA.Id, group, null, ambalaje.Id));
        var notInDepot = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveDepotRule(halaB.Id, group, null, ambalaje.Id)));
        Assert.Equal("INV_INVALID_REFERENCE", notInDepot.Code);

        Assert.Equal(ambalaje.Id, await WithService<IWarehouseLookup, int?>(l => l.ProposeWarehouseAsync(halaA.Id, boxVariants[0], Ct)));
        Assert.Equal(marfa.Id, await WithService<IWarehouseLookup, int?>(l => l.ProposeWarehouseAsync(halaA.Id, waterVariants[0], Ct)));
        Assert.Null(await WithService<IWarehouseLookup, int?>(l => l.ProposeWarehouseAsync(halaB.Id, waterVariants[0], Ct)));
        var depot = await WithService<IWarehouseLookup, DepotDto?>(l => l.GetDepotAsync(halaA.Id, Ct));
        Assert.Equal(new[] { marfa.Id, ambalaje.Id }.Order(), depot!.WarehouseIds.Order());

        // Locatia cu depozite active nu revine la nivelul „gestiune”.
        var back = await Assert.ThrowsAsync<ErpException>(() => Send(new SetStockLevel(null, ploiesti, StockKeepingLevel.Warehouse)));
        Assert.Equal("INV_DEPOT_LOCATION_INVALID", back.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A15")]
    public async Task Levels_cannot_contradict_existing_links()
    {
        var cluj = await NewLocation(Unique("CJ"));
        var depotInWarehouseLevel = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveDepot(null, Unique("H"), "Hala", cluj, null, true, null)));
        Assert.Equal("INV_DEPOT_LOCATION_INVALID", depotInWarehouseLevel.Code);

        await Warehouse(Unique("DEP-"), cluj);
        var withWarehouses = await Assert.ThrowsAsync<ErpException>(() => Send(new SetStockLevel(null, cluj, StockKeepingLevel.Depot)));
        Assert.Equal("INV_DEPOT_LOCATION_INVALID", withWarehouses.Code);
        Assert.Equal(StockKeepingLevel.Warehouse, (await Send(new GetDepots())).Locations.Single(l => l.Id == cluj).Level);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-6")]
    public async Task The_lookup_lists_the_users_warehouses_and_the_holder_on_a_date()
    {
        var saved = await Warehouse(Unique("LK-"), Database.ClujDepotId);
        var keeper = await NewEmployee("Gheorghe", "Ilie");
        await Send(new SaveWarehouseKeeper(null, saved.Id, keeper, 1, new DateOnly(2026, 2, 1), null, null, null));
        Assert.Equal(keeper, await WithService<IWarehouseLookup, int?>(l => l.GetKeeperEmployeeIdAsync(saved.Id, new DateOnly(2026, 11, 28), Ct)));
        Assert.Null(await WithService<IWarehouseLookup, int?>(l => l.GetKeeperEmployeeIdAsync(saved.Id, new DateOnly(2026, 1, 15), Ct)));

        var dto = await WithService<IWarehouseLookup, WarehouseDto?>(l => l.GetAsync(saved.Id, Ct));
        WarehouseScope = [dto!.DimensionValueId];
        var mine = await WithService<IWarehouseLookup, IReadOnlyList<WarehouseDto>>(l => l.ListForUserAsync(Database.CompanyId, Ct));
        Assert.Equal(saved.Id, Assert.Single(mine).Id);
    }
}
