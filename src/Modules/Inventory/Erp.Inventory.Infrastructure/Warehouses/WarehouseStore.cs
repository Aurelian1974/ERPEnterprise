using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Settings;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;

namespace Erp.Inventory.Infrastructure.Warehouses;

/// <summary>Gestiunile prin procedurile inv.usp_Warehouse_* (NET-06).</summary>
public sealed class WarehouseStore(IDbSession session) : IWarehouseStore
{
    public async Task<IReadOnlyList<WarehouseItemDto>> ListAsync(int companyId, string? search, WarehouseKind? kind, int? locationId,
        bool includeInactive, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(Command("inv.usp_Warehouse_List", new
        {
            CompanyId = companyId, Search = search, Kind = (byte?)kind, LocationId = locationId, IncludeInactive = includeInactive,
        }, ct)).ConfigureAwait(false))
        .Select(r => new WarehouseItemDto(r.Id, r.Code, r.Name, (WarehouseKind)r.Kind, (ValuationBasis)r.ValuationBasis, r.LocationId, r.LocationName,
            r.ThirdPartyId, r.ThirdPartyName, (NegativeStockPolicy)r.NegativeStockPolicy, (WarehousePurpose)r.Purpose, r.DimensionValueId, r.IsBlocked,
            r.IsActive))
        .ToList();

    public async Task<WarehouseLookupsDto> GetLookupsAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_Warehouse_Lookups", new { CompanyId = companyId }, ct))
            .ConfigureAwait(false);
        var locations = (await grid.ReadAsync<WarehouseLocationDto>().ConfigureAwait(false)).ToList();
        var costCenters = (await grid.ReadAsync<WarehouseOptionDto>().ConfigureAwait(false)).ToList();
        var returns = (await grid.ReadAsync<WarehouseOptionDto>().ConfigureAwait(false)).ToList();
        return new WarehouseLookupsDto(locations, costCenters, returns);
    }

    public async Task<WarehouseDetailDto?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command("inv.usp_Warehouse_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        return r is null ? null : new WarehouseDetailDto(r.Id, r.CompanyId, r.Code, r.Name, r.LocationId, r.LocationName, (WarehouseKind)r.Kind,
            (ValuationBasis)r.ValuationBasis, r.RetailPriceListId, r.IsOwnGoods, r.ThirdPartyId, r.ThirdPartyName, (NegativeStockPolicy)r.NegativeStockPolicy,
            r.DimensionValueId, r.DefaultCostCenterId, r.IsBlocked, r.BlockReason, r.BlockedAt, r.DeliveryOrderMode, r.ReturnWarehouseId,
            (WarehousePurpose)r.Purpose, r.IsActive, r.FirstMovementAt is not null, r.CreatedAt, r.ModifiedAt, r.ModifiedByName, r.RowVersion);
    }

    public Task<WarehouseWritten> SaveAsync(WarehouseRecord w, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<WarehouseWritten>(Command("inv.usp_Warehouse_Save", new
        {
            w.Id, w.CompanyId, w.Code, w.Name, w.LocationId, Kind = (byte)w.Kind, ValuationBasis = (byte)w.ValuationBasis, w.RetailPriceListId,
            w.ThirdPartyId, NegativeStockPolicy = (byte?)w.NegativeStockPolicy, w.DimensionValueId, w.DefaultCostCenterId, w.DeliveryOrderMode,
            w.ReturnWarehouseId, Purpose = (byte)w.Purpose, w.RowVersion, UserId = userId,
        }, ct));

    public Task<WarehouseWritten> SetBlockedAsync(int companyId, int id, bool isBlocked, string? reason, byte[]? rowVersion, int userId,
        CancellationToken ct) =>
        session.Connection.QuerySingleAsync<WarehouseWritten>(Command("inv.usp_Warehouse_SetBlocked", new
        {
            CompanyId = companyId, Id = id, IsBlocked = isBlocked, Reason = reason, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public Task<WarehouseWritten> SetActiveAsync(int companyId, int id, bool isActive, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<WarehouseWritten>(Command("inv.usp_Warehouse_SetActive", new
        {
            CompanyId = companyId, Id = id, IsActive = isActive, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<InventoryHistoryEntryDto>> HistoryAsync(int companyId, int id, CancellationToken ct) =>
        (await session.Connection.QueryAsync<InventoryHistoryEntryDto>(Command("inv.usp_Warehouse_History", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false)).ToList();

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record ItemRow(int Id, string Code, string Name, byte Kind, byte ValuationBasis, int? LocationId, string? LocationName,
        int? ThirdPartyId, string? ThirdPartyName, byte NegativeStockPolicy, byte Purpose, int DimensionValueId, bool IsBlocked, bool IsActive);

    private sealed record DetailRow(int Id, int CompanyId, string Code, string Name, int? LocationId, string? LocationName, byte Kind,
        byte ValuationBasis, int? RetailPriceListId, bool IsOwnGoods, int? ThirdPartyId, string? ThirdPartyName, byte NegativeStockPolicy,
        int DimensionValueId, int? DefaultCostCenterId, bool IsBlocked, string? BlockReason, DateTime? BlockedAt, byte DeliveryOrderMode,
        int? ReturnWarehouseId, byte Purpose, bool IsActive, DateTime? FirstMovementAt, DateTime CreatedAt, DateTime? ModifiedAt,
        string? ModifiedByName, byte[] RowVersion);
}

/// <summary>Setarile de stoc ale firmei prin inv.usp_StockSettings_*.</summary>
public sealed class StockSettingsStore(IDbSession session) : IStockSettingsStore
{
    public async Task<StockSettingsDto?> GetAsync(int companyId, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("inv.usp_StockSettings_Get", new { CompanyId = companyId },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        return r is null ? null : new StockSettingsDto((ValuationMethod)r.DefaultValuationMethod, (StockKeepingLevel)r.StockKeepingLevel,
            (NegativeStockPolicy)r.DefaultNegativeStockPolicy, r.RequireCountOnHandover, r.BackdatingDays, r.BackdatingWithRight, r.TransferMode,
            r.CostRecalcScope, r.LandedCostAllocation, r.ExpiryPickRule, r.ExpiryWarningDays, r.ReservationExpiryDays, r.CountFreezeMode, r.RowVersion);
    }

    public Task<InventorySaved> SaveAsync(int companyId, SaveStockSettings s, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(new CommandDefinition("inv.usp_StockSettings_Save", new
        {
            CompanyId = companyId, DefaultValuationMethod = (byte)s.DefaultValuationMethod, DefaultNegativeStockPolicy = (byte)s.DefaultNegativeStockPolicy,
            s.RequireCountOnHandover, s.BackdatingDays, s.BackdatingWithRight, s.TransferMode, s.CostRecalcScope, s.LandedCostAllocation,
            s.ExpiryPickRule, s.ExpiryWarningDays, s.ReservationExpiryDays, s.CountFreezeMode, s.RowVersion, UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    private sealed record Row(byte DefaultValuationMethod, byte StockKeepingLevel, byte DefaultNegativeStockPolicy, bool RequireCountOnHandover,
        short BackdatingDays, bool BackdatingWithRight, byte TransferMode, byte CostRecalcScope, byte LandedCostAllocation, byte ExpiryPickRule,
        short ExpiryWarningDays, short ReservationExpiryDays, byte CountFreezeMode, byte[] RowVersion);
}

/// <summary>
/// Initializarea firmei in Stocuri (S1 5.2): valoarea de dimensiune „gestiune” TRANZIT prin Nucleu, apoi setarile si gestiunea de tranzit,
/// in tranzactia apelantului. Nu face nimic daca firma are deja setarile si tranzitul.
/// </summary>
public sealed class StockCompanySetup(IDbSession session, IDimensionLookup dimensions, ICurrentContext context) : IStockCompanySetup
{
    public async Task EnsureAsync(int companyId, CancellationToken ct)
    {
        var ready = await session.Connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM inv.StockSettings WHERE CompanyId = @CompanyId)
                              AND EXISTS (SELECT 1 FROM inv.Warehouse WHERE CompanyId = @CompanyId AND Kind = 3) THEN 1 ELSE 0 END AS bit);
            """, new { CompanyId = companyId }, session.Transaction, cancellationToken: ct)).ConfigureAwait(false);
        if (ready)
        {
            return;
        }

        var transit = await dimensions.EnsureAsync(companyId, DimensionTypes.Warehouse, "TRANZIT", "Tranzit", ct).ConfigureAwait(false);
        await session.Connection.ExecuteAsync(new CommandDefinition("inv.usp_StockSettings_Ensure",
            new { CompanyId = companyId, TransitDimensionValueId = transit, UserId = context.UserId == 0 ? 1 : context.UserId }, session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
    }
}
