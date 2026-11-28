using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Settings;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Infrastructure.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Gestiunile si setarile de stoc ale firmei pe SQL Server real (02.05, S1 3.1, 3.3, 3.7, 4.1–4.4), pe baza case sensitive creata pe un server
/// case insensitive (S1-A13).
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class WarehouseIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private SaveWarehouse Depot(string code, int? locationId = null) =>
        new(null, code, "Gestiune " + code, locationId ?? Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1, null,
            WarehousePurpose.Regular, null);

    [Fact]
    [Trait("Criteriu", "02.05-S1-A1")]
    [Trait("Criteriu", "02.05-S1-A13")]
    public async Task The_code_is_unique_per_company_without_case_and_free_in_another_company()
    {
        var code = Unique("DEP-CJ");
        await Send(Depot(code));

        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(Depot(code.ToLowerInvariant())));
        Assert.Equal("INV_WAREHOUSE_CODE_EXISTS", duplicate.Code);

        var other = await Send(Depot(code, Database.OtherCompanyLocationId), companyId: Database.OtherCompanyId);
        Assert.True(other.Id > 0);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A2")]
    public async Task Retail_price_valuation_needs_a_store_with_a_price_list()
    {
        var store = new SaveWarehouse(null, Unique("MAG-"), "Magazin Cluj", Database.ClujStoreId, WarehouseKind.Store, ValuationBasis.RetailPrice, null,
            null, null, null, 1, null, WarehousePurpose.Regular, null);
        var noList = await Assert.ThrowsAsync<ErpException>(() => Send(store));
        Assert.Equal("INV_WAREHOUSE_RETAIL_PRICELIST_REQUIRED", noList.Code);

        var depot = await Assert.ThrowsAsync<ErpException>(() => Send(Depot(Unique("DEP-")) with
        {
            ValuationBasis = ValuationBasis.RetailPrice, RetailPriceListId = 7,
        }));
        Assert.Equal("INV_WAREHOUSE_RETAIL_PRICELIST_REQUIRED", depot.Code);

        var saved = await Send(store with { RetailPriceListId = 7 });
        var detail = await Send(new GetWarehouse(saved.Id));
        Assert.Equal((ValuationBasis.RetailPrice, 7, "Magazin Cluj"), (detail.ValuationBasis, detail.RetailPriceListId, detail.LocationName));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A4")]
    public async Task The_warehouse_dimension_value_is_created_with_it_and_rolled_back_with_it()
    {
        var code = Unique("DC-PL");
        var saved = await Send(Depot(code, Database.PloiestiId) with { Name = "Depozit central Ploiești" });
        var dimension = await Query<(string Type, string Code, string Name, bool IsActive)>(
            "SELECT v.DimensionType, v.Code, v.Name, v.IsActive FROM inv.Warehouse AS w JOIN core.DimensionValue AS v ON v.Id = w.DimensionValueId WHERE w.Id = @Id",
            new { saved.Id });
        Assert.Equal(("WAREHOUSE", code, "Depozit central Ploiești", true), dimension);

        // Refuzat in procedura (locatia altei firme), dupa crearea valorii: rollback-ul nu lasa valoarea orfana.
        var orphan = Unique("ORF-");
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(Depot(orphan, Database.OtherCompanyLocationId)));
        Assert.Equal("INV_WAREHOUSE_LOCATION_INVALID", refused.Code);
        Assert.Equal(0, await Query<int>("SELECT COUNT(*) FROM core.DimensionValue WHERE CodeKey = UPPER(@Code)", new { Code = orphan }));

        // Redenumirea gestiunii se aliniaza in valoarea de dimensiune.
        var detail = await Send(new GetWarehouse(saved.Id));
        await Send(Depot(code + "X", Database.PloiestiId) with { Id = saved.Id, Name = "DC Ploiești", RowVersion = detail.RowVersion });
        Assert.Equal((code + "X", "DC Ploiești"), await Query<(string, string)>(
            "SELECT Code, Name FROM core.DimensionValue WHERE Id = @Id", new { Id = detail.DimensionValueId }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-3.1")]
    public async Task Kind_rules_location_third_party_and_negative_policy_are_checked()
    {
        var closed = await Assert.ThrowsAsync<ErpException>(() => Send(Depot(Unique("V-"), Database.InactiveLocationId)));
        Assert.Equal("INV_WAREHOUSE_LOCATION_INVALID", closed.Code);

        var custody = new SaveWarehouse(null, Unique("CUST-"), "Custodie Ploiești", Database.PloiestiId, WarehouseKind.Custody, ValuationBasis.Cost,
            null, Database.LogisticPartnerId, NegativeStockPolicy.AllowedWithWarning, null, 1, null, WarehousePurpose.Regular, null);
        var permissive = await Assert.ThrowsAsync<ErpException>(() => Send(custody));
        Assert.Equal("INV_NEGATIVE_POLICY_NOT_ALLOWED", permissive.Code);
        var saved = await Send(custody with { NegativeStockPolicy = null });
        var detail = await Send(new GetWarehouse(saved.Id));
        Assert.Equal((false, NegativeStockPolicy.Blocked, "Logistic Alfa SRL"), (detail.IsOwnGoods, detail.NegativeStockPolicy, detail.ThirdPartyName));

        var atThirdParty = new SaveWarehouse(null, Unique("LA-"), "La Logistic Alfa", Database.PloiestiId, WarehouseKind.AtThirdParty,
            ValuationBasis.Cost, null, Database.LogisticPartnerId, null, null, 1, null, WarehousePurpose.Regular, null);
        var withLocation = await Assert.ThrowsAsync<ErpException>(() => Send(atThirdParty));
        Assert.Equal("INV_WAREHOUSE_LOCATION_INVALID", withLocation.Code);
        Assert.True((await Send(atThirdParty with { LocationId = null })).Id > 0);

        var transit = await Assert.ThrowsAsync<ErpValidationException>(() => Send(Depot(Unique("T-")) with { Kind = WarehouseKind.Transit, LocationId = null }));
        Assert.Contains("kind", transit.Errors.Keys);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A12")]
    public async Task Deactivation_follows_the_dimension_and_the_transit_warehouse_stays_active()
    {
        var saved = await Send(Depot(Unique("DZ-")));
        var deactivated = await Send(new DeactivateWarehouse(saved.Id, saved.RowVersion));
        Assert.False(await Query<bool>("SELECT v.IsActive FROM inv.Warehouse AS w JOIN core.DimensionValue AS v ON v.Id = w.DimensionValueId WHERE w.Id = @Id",
            new { saved.Id }));
        var stale = await Assert.ThrowsAsync<ErpException>(() => Send(new ReactivateWarehouse(saved.Id, saved.RowVersion)));
        Assert.Equal("INV_CONCURRENCY_CONFLICT", stale.Code);
        await Send(new ReactivateWarehouse(saved.Id, deactivated.RowVersion));
        Assert.True((await Send(new GetWarehouse(saved.Id))).IsActive);

        var transit = (await Send(new GetWarehouses(Kind: WarehouseKind.Transit))).Warehouses.Single();
        Assert.Equal("TRANZIT", transit.Code);
        var detail = await Send(new GetWarehouse(transit.Id));
        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new DeactivateWarehouse(transit.Id, detail.RowVersion)));
        Assert.Equal("INV_WAREHOUSE_NOT_EMPTY", refused.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-4.3")]
    public async Task Blocking_needs_a_reason_and_publishes_the_warehouse_event()
    {
        var saved = await Send(Depot(Unique("BL-")));
        var noReason = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new BlockWarehouse(saved.Id, " ", saved.RowVersion)));
        Assert.Contains("reason", noReason.Errors.Keys);

        var blocked = await Send(new BlockWarehouse(saved.Id, "Inventar anual", saved.RowVersion));
        var detail = await Send(new GetWarehouse(saved.Id));
        Assert.Equal((true, "Inventar anual"), (detail.IsBlocked, detail.BlockReason));
        Assert.True(await Query<int>("""
            SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%GestiuneModificata%' AND Payload LIKE N'%"isBlocked":true%' AND Payload LIKE @Code
            """, new { Code = "%" + detail.Code + "%" }) >= 1);

        await Send(new UnblockWarehouse(saved.Id, blocked.RowVersion));
        Assert.False((await Send(new GetWarehouse(saved.Id))).IsBlocked);
        Assert.NotEmpty(await Send(new GetWarehouseHistory(saved.Id)));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A5")]
    public async Task A_user_scoped_to_one_warehouse_sees_only_it()
    {
        var allowed = await Send(Depot(Unique("SC-")));
        var hidden = await Send(Depot(Unique("SC-")));
        var allowedDimension = (await Send(new GetWarehouse(allowed.Id))).DimensionValueId;

        WarehouseScope = [allowedDimension];
        var page = await Send(new GetWarehouses());
        Assert.Contains(page.Warehouses, w => w.Id == allowed.Id);
        Assert.DoesNotContain(page.Warehouses, w => w.Id == hidden.Id);
        var notVisible = await Assert.ThrowsAsync<ErpException>(() => Send(new GetWarehouse(hidden.Id)));
        Assert.Equal(InventoryErrorCodes.WarehouseNotFound, notVisible.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-3.3")]
    public async Task Company_settings_start_with_the_defaults_and_the_first_company_event_is_idempotent()
    {
        await InTransaction(sp => new InitializeCompanyStock(sp.GetRequiredService<IStockCompanySetup>())
            .HandleAsync(new FirmaModificata("14399840", "Exemplu Producție SRL", true, false) { CompanyId = Database.OtherCompanyId }, Ct),
            Database.OtherCompanyId);
        await InTransaction(sp => sp.GetRequiredService<IStockCompanySetup>().EnsureAsync(Database.OtherCompanyId, Ct), Database.OtherCompanyId);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM inv.Warehouse WHERE CompanyId = @Id AND Kind = 3", new { Id = Database.OtherCompanyId }));

        var settings = await Send(new GetStockSettings(), companyId: Database.OtherCompanyId);
        Assert.Equal((ValuationMethod.WeightedAverage, StockKeepingLevel.Warehouse, NegativeStockPolicy.Blocked, true, (short)5, (short)30, (short)14),
            (settings.DefaultValuationMethod, settings.StockKeepingLevel, settings.DefaultNegativeStockPolicy, settings.RequireCountOnHandover,
                settings.BackdatingDays, settings.ExpiryWarningDays, settings.ReservationExpiryDays));

        await Send(new SaveStockSettings(ValuationMethod.Fifo, NegativeStockPolicy.AllowedWithWarning, false, 0, false, 2, 1, 1, 1, 15, 7, 2,
            settings.RowVersion), companyId: Database.OtherCompanyId);
        var stale = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveStockSettings(ValuationMethod.Fifo, NegativeStockPolicy.Blocked, false, 0,
            false, 2, 1, 1, 1, 15, 7, 2, settings.RowVersion), companyId: Database.OtherCompanyId));
        Assert.Equal("INV_CONCURRENCY_CONFLICT", stale.Code);

        // Gestiunile noi primesc politica implicita a firmei.
        var saved = await Send(Depot(Unique("NP-"), Database.OtherCompanyLocationId), companyId: Database.OtherCompanyId);
        Assert.Equal(NegativeStockPolicy.AllowedWithWarning, (await Send(new GetWarehouse(saved.Id), companyId: Database.OtherCompanyId)).NegativeStockPolicy);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A3")]
    public async Task After_the_first_movement_code_kind_and_valuation_basis_are_locked()
    {
        var saved = await Send(Depot(Unique("FM-")));
        await Execute("UPDATE inv.Warehouse SET FirstMovementAt = SYSUTCDATETIME() WHERE Id = @Id", new { saved.Id });
        var detail = await Send(new GetWarehouse(saved.Id));
        Assert.True(detail.HasMovements);

        var kind = await Assert.ThrowsAsync<ErpException>(() => Send(Depot(detail.Code) with
        {
            Id = saved.Id, Kind = WarehouseKind.Store, RowVersion = detail.RowVersion,
        }));
        Assert.Equal("INV_WAREHOUSE_LOCKED_FIELD", kind.Code);
        var code = await Assert.ThrowsAsync<ErpException>(() => Send(Depot(detail.Code + "2") with { Id = saved.Id, RowVersion = detail.RowVersion }));
        Assert.Equal("INV_WAREHOUSE_LOCKED_FIELD", code.Code);
        // Mutarea intr-o locatie de alt tip (magazin) e refuzata; numele se poate schimba.
        var moved = await Assert.ThrowsAsync<ErpException>(() => Send(Depot(detail.Code, Database.ClujStoreId) with
        {
            Id = saved.Id, RowVersion = detail.RowVersion,
        }));
        Assert.Equal("INV_WAREHOUSE_LOCATION_INVALID", moved.Code);
        await Send(Depot(detail.Code, Database.PloiestiId) with { Id = saved.Id, Name = "Redenumită", RowVersion = detail.RowVersion });
    }
}
