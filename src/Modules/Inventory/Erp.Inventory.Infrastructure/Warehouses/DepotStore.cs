using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;

namespace Erp.Inventory.Infrastructure.Warehouses;

/// <summary>Depozitele si nivelul de evidenta prin inv.usp_Depot_*, inv.usp_DepotWarehouse*_Save, inv.usp_StockLevel_* (NET-06).</summary>
public sealed class DepotStore(IDbSession session) : IDepotStore
{
    public async Task<DepotPageDto> GetPageAsync(int companyId, CancellationToken ct)
    {
        StockKeepingLevel companyLevel;
        List<StockLevelOverrideDto> overrides;
        List<LocationLevelDto> locations;
        using (var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_StockLevel_Get", new { CompanyId = companyId }, ct))
            .ConfigureAwait(false))
        {
            companyLevel = (StockKeepingLevel)await grid.ReadSingleAsync<byte>().ConfigureAwait(false);
            overrides = (await grid.ReadAsync<OverrideRow>().ConfigureAwait(false)).Select(o => new StockLevelOverrideDto(o.Id, o.BranchId, o.BranchName,
                o.LocationId, o.LocationName, (StockKeepingLevel)o.StockKeepingLevel)).ToList();
            locations = (await grid.ReadAsync<LocationRow>().ConfigureAwait(false)).Select(l => new LocationLevelDto(l.Id, l.Code, l.Name, l.BranchId,
                (StockKeepingLevel)l.StockKeepingLevel, l.Source)).ToList();
        }

        using var depots = await session.Connection.QueryMultipleAsync(Command("inv.usp_Depot_List", new { CompanyId = companyId }, ct))
            .ConfigureAwait(false);
        var rows = (await depots.ReadAsync<DepotRow>().ConfigureAwait(false)).ToList();
        var links = (await depots.ReadAsync<LinkRow>().ConfigureAwait(false)).ToList();
        var rules = (await depots.ReadAsync<DepotRuleDto>().ConfigureAwait(false)).ToList();
        var items = rows.Select(d => new DepotItemDto(d.Id, d.Code, d.Name, d.LocationId, d.LocationName, d.DefaultWarehouseId, d.DimensionValueId,
            d.IsBlocked, d.BlockReason, d.IsActive,
            links.Where(l => l.DepotId == d.Id).Select(l => new WarehouseOptionDto(l.WarehouseId, l.WarehouseCode, l.WarehouseName)).ToList(),
            d.RowVersion)).ToList();
        return new DepotPageDto(companyLevel, overrides, locations, items, rules);
    }

    public Task<WarehouseWritten> SaveAsync(DepotRecord d, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<WarehouseWritten>(Command("inv.usp_Depot_Save", new
        {
            d.Id, d.CompanyId, d.Code, d.Name, d.LocationId, d.DefaultWarehouseId, d.DimensionValueId, d.IsActive, d.RowVersion, UserId = userId,
        }, ct));

    public Task<WarehouseWritten> SetBlockedAsync(int companyId, int id, bool isBlocked, string? reason, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<WarehouseWritten>(Command("inv.usp_Depot_SetBlocked", new
        {
            CompanyId = companyId, Id = id, IsBlocked = isBlocked, Reason = reason, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public Task SetWarehouseAsync(int companyId, int depotId, int warehouseId, bool isActive, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("inv.usp_DepotWarehouse_Save", new
        {
            CompanyId = companyId, DepotId = depotId, WarehouseId = warehouseId, IsActive = isActive, UserId = userId,
        }, ct));

    public Task SaveRuleAsync(int companyId, int depotId, int? itemGroupId, int? itemId, int? warehouseId, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("inv.usp_DepotWarehouseRule_Save", new
        {
            CompanyId = companyId, DepotId = depotId, ItemGroupId = itemGroupId, ItemId = itemId, WarehouseId = warehouseId, UserId = userId,
        }, ct));

    public Task SetLevelAsync(int companyId, int? branchId, int? locationId, StockKeepingLevel? level, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("inv.usp_StockLevel_Set", new
        {
            CompanyId = companyId, BranchId = branchId, LocationId = locationId, Level = (byte?)level, UserId = userId,
        }, ct));

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record OverrideRow(int Id, int? BranchId, string? BranchName, int? LocationId, string? LocationName, byte StockKeepingLevel);

    private sealed record LocationRow(int Id, string Code, string Name, int? BranchId, byte StockKeepingLevel, byte Source);

    private sealed record DepotRow(int Id, string Code, string Name, int LocationId, string LocationName, int? DefaultWarehouseId, int DimensionValueId,
        bool IsBlocked, string? BlockReason, bool IsActive, byte[] RowVersion);

    private sealed record LinkRow(int DepotId, int WarehouseId, string WarehouseCode, string WarehouseName);
}

/// <summary>IWarehouseLookup (S1 6) peste procedurile Stocurilor; ListForUserAsync aplica scope-ul pe gestiuni al utilizatorului curent.</summary>
public sealed class WarehouseLookup(IDbSession session, ICurrentContext context) : IWarehouseLookup
{
    public async Task<WarehouseDto?> GetAsync(int warehouseId, CancellationToken ct) =>
        await session.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT Id, CompanyId, Code, Name, Kind, ValuationBasis, LocationId, ThirdPartyId, DimensionValueId, NegativeStockPolicy, IsBlocked, IsActive
            FROM inv.Warehouse WHERE Id = @Id;
            """, new { Id = warehouseId }, session.Transaction, cancellationToken: ct)).ConfigureAwait(false) is { } r ? Map(r) : null;

    public async Task<IReadOnlyList<WarehouseDto>> ListForUserAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT Id, CompanyId, Code, Name, Kind, ValuationBasis, LocationId, ThirdPartyId, DimensionValueId, NegativeStockPolicy, IsBlocked, IsActive
            FROM inv.Warehouse WHERE CompanyId = @CompanyId AND IsActive = 1 ORDER BY CodeKey;
            """, new { CompanyId = companyId }, session.Transaction, cancellationToken: ct)).ConfigureAwait(false))
        .Where(r => context.HasPermission(Application.InventoryPermissions.WarehouseView, new DimensionRef(DimensionTypes.Warehouse, r.DimensionValueId)))
        .Select(Map).ToList();

    public Task<int?> GetKeeperEmployeeIdAsync(int warehouseId, DateOnly onDate, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("inv.usp_WarehouseLookup_Keeper",
            new { WarehouseId = warehouseId, OnDate = onDate.ToDateTime(TimeOnly.MinValue) }, session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

    public async Task<StockKeepingLevel> GetLevelAsync(int locationId, CancellationToken ct) =>
        (StockKeepingLevel)await session.Connection.QuerySingleAsync<byte>(new CommandDefinition("""
            SELECT ISNULL((SELECT e.StockKeepingLevel FROM core.Location AS l CROSS APPLY inv.ufn_LocationStockLevel(l.CompanyId) AS e
                           WHERE l.Id = @Id AND e.LocationId = l.Id), 1);
            """, new { Id = locationId }, session.Transaction, cancellationToken: ct)).ConfigureAwait(false);

    public async Task<DepotDto?> GetDepotAsync(int depotId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("""
            SELECT Id, CompanyId, Code, Name, LocationId, DefaultWarehouseId, IsBlocked, IsActive FROM inv.Depot WHERE Id = @Id;
            SELECT WarehouseId FROM inv.DepotWarehouse WHERE DepotId = @Id AND IsActive = 1 ORDER BY WarehouseId;
            """, new { Id = depotId }, session.Transaction, cancellationToken: ct)).ConfigureAwait(false);
        var d = await grid.ReadSingleOrDefaultAsync<DepotRow>().ConfigureAwait(false);
        var warehouses = (await grid.ReadAsync<int>().ConfigureAwait(false)).ToList();
        return d is null ? null : new DepotDto(d.Id, d.CompanyId, d.Code, d.Name, d.LocationId, d.DefaultWarehouseId, warehouses, d.IsBlocked, d.IsActive);
    }

    public Task<int?> ProposeWarehouseAsync(int depotId, int itemVariantId, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("inv.usp_Depot_ProposeWarehouse",
            new { DepotId = depotId, ItemVariantId = itemVariantId }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    private static WarehouseDto Map(Row r) => new(r.Id, r.CompanyId, r.Code, r.Name, (WarehouseKind)r.Kind, (ValuationBasis)r.ValuationBasis, r.LocationId,
        r.ThirdPartyId, r.DimensionValueId, (NegativeStockPolicy)r.NegativeStockPolicy, r.IsBlocked, r.IsActive);

    private sealed record Row(int Id, int CompanyId, string Code, string Name, byte Kind, byte ValuationBasis, int? LocationId, int? ThirdPartyId,
        int DimensionValueId, byte NegativeStockPolicy, bool IsBlocked, bool IsActive);

    private sealed record DepotRow(int Id, int CompanyId, string Code, string Name, int LocationId, int? DefaultWarehouseId, bool IsBlocked, bool IsActive);
}
