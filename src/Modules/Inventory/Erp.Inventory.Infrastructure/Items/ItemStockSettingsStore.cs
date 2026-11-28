using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;

namespace Erp.Inventory.Infrastructure.Items;

/// <summary>Setarile de stoc ale articolului prin inv.usp_ItemStockSettings_* si inv.usp_ItemWarehouseSettings_* (NET-06).</summary>
public sealed class ItemStockSettingsStore(IDbSession session) : IItemStockSettingsStore
{
    public async Task<ItemStockTabDto> GetAsync(int companyId, int itemId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_ItemStockSettings_Get",
            new { CompanyId = companyId, ItemId = itemId }, ct)).ConfigureAwait(false);
        var c = await grid.ReadSingleAsync<CompanyRow>().ConfigureAwait(false);
        var company = new ItemCompanyStockDto(c.Id, c.DefaultWarehouseId, (ValuationMethod)c.ValuationMethod, (TrackingMode)c.TrackingMode,
            Quantity.Format(c.MinQty), Quantity.Format(c.MaxQty), (ReplenishmentMethod)c.ReplenishmentMethod, c.IsBlockedForIssue, c.RowVersion);
        var warehouses = (await grid.ReadAsync<WarehouseRow>().ConfigureAwait(false)).Select(w => new ItemWarehouseStockDto(w.Id, w.WarehouseId,
            w.WarehouseCode, w.WarehouseName, w.ItemVariantId, w.VariantCode, Quantity.Format(w.MinQty), Quantity.Format(w.MaxQty),
            Quantity.Format(w.ReorderQty), (ReplenishmentMethod?)w.ReplenishmentMethod,
            w.IsBlockedForIssue, w.IsBlockedForReceipt, w.IsListed, w.RowVersion)).ToList();
        var variants = (await grid.ReadAsync<WarehouseOptionDto>().ConfigureAwait(false)).ToList();
        var defaults = (await grid.ReadAsync<WarehouseOptionDto>().ConfigureAwait(false)).ToList();
        return new ItemStockTabDto(company, warehouses, variants, defaults);
    }

    public Task<InventorySaved> SaveAsync(int companyId, SaveItemStockSettings s, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_ItemStockSettings_Save", new
        {
            CompanyId = companyId, s.ItemId, s.DefaultWarehouseId, ValuationMethod = (byte)s.ValuationMethod, TrackingMode = (byte)s.TrackingMode,
            MinQty = Quantity.ParseOptional(s.MinQty), MaxQty = Quantity.ParseOptional(s.MaxQty), ReplenishmentMethod = (byte?)s.ReplenishmentMethod,
            s.IsBlockedForIssue, s.RowVersion, UserId = userId,
        }, ct));

    public Task<InventorySaved> SaveWarehouseAsync(int companyId, SaveItemWarehouseSettings s, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_ItemWarehouseSettings_Save", new
        {
            CompanyId = companyId, s.Id, s.WarehouseId, s.ItemId, s.ItemVariantId, MinQty = Quantity.ParseOptional(s.MinQty),
            MaxQty = Quantity.ParseOptional(s.MaxQty), ReorderQty = Quantity.ParseOptional(s.ReorderQty),
            ReplenishmentMethod = (byte?)s.ReplenishmentMethod, s.IsBlockedForIssue, s.IsBlockedForReceipt, s.IsListed, s.RowVersion, UserId = userId,
        }, ct));

    public Task DeleteWarehouseAsync(int companyId, int id, byte[] rowVersion, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("inv.usp_ItemWarehouseSettings_Delete", new { CompanyId = companyId, Id = id, RowVersion = rowVersion }, ct));

    public Task EnsureDefaultsAsync(int itemId, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("inv.usp_ItemStockSettings_EnsureDefaults", new { ItemId = itemId, UserId = userId }, ct));

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record CompanyRow(int? Id, int? DefaultWarehouseId, byte ValuationMethod, byte TrackingMode, decimal? MinQty, decimal? MaxQty,
        byte ReplenishmentMethod, bool IsBlockedForIssue, byte[]? RowVersion);

    private sealed record WarehouseRow(int Id, int WarehouseId, string WarehouseCode, string WarehouseName, int? ItemVariantId, string? VariantCode,
        decimal? MinQty, decimal? MaxQty, decimal? ReorderQty, byte? ReplenishmentMethod, bool IsBlockedForIssue, bool IsBlockedForReceipt, bool IsListed,
        byte[] RowVersion);
}

/// <summary>
/// IStockPolicyProvider (S1 6): straturile din inv.usp_StockPolicy_Layers (un apel, TVP) si precedenta din StockPolicyRules. Firma e cea
/// curenta; varianta inexistenta lipseste din rezultat.
/// </summary>
public sealed class StockPolicyProvider(IDbSession session, ICurrentContext context) : IStockPolicyProvider
{
    public async Task<ItemStockPolicyDto> GetAsync(int warehouseId, int itemVariantId, CancellationToken ct) =>
        (await GetManyAsync(warehouseId, [itemVariantId], ct).ConfigureAwait(false)).TryGetValue(itemVariantId, out var policy)
            ? policy : throw ErpException.NotFound("INV_ITEM_VARIANT_NOT_FOUND", "Varianta articolului nu există.");

    public async Task<IReadOnlyDictionary<int, ItemStockPolicyDto>> GetManyAsync(int warehouseId, IReadOnlyCollection<int> itemVariantIds,
        CancellationToken ct)
    {
        var ids = new DataTable();
        ids.Columns.Add("Id", typeof(int));
        foreach (var id in itemVariantIds.Distinct())
        {
            ids.Rows.Add(id);
        }

        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("inv.usp_StockPolicy_Layers", new
        {
            context.CompanyId, WarehouseId = warehouseId, Variants = ids.AsTableValuedParameter("inv.IdList"),
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var w = await grid.ReadSingleAsync<WarehouseRow>().ConfigureAwait(false);
        var warehouse = new WarehousePolicyLayer(warehouseId, (WarehouseKind)w.Kind, (NegativeStockPolicy)w.NegativeStockPolicy,
            (ValuationMethod)w.DefaultValuationMethod);
        var variants = (await grid.ReadAsync<VariantRow>().ConfigureAwait(false)).ToList();
        var locals = (await grid.ReadAsync<LocalRow>().ConfigureAwait(false)).ToList();
        return variants.ToDictionary(v => v.ItemVariantId, v => StockPolicyRules.Resolve(warehouse, v.ItemId, v.ItemVariantId,
            v.ValuationMethod is { } method
                ? new CompanyItemLayer((ValuationMethod)method, (TrackingMode)(v.TrackingMode ?? 0), v.MinQty, v.MaxQty,
                    (ReplenishmentMethod)(v.ReplenishmentMethod ?? 0), v.IsBlockedForIssue ?? false)
                : null,
            Layer(locals.FirstOrDefault(l => l.ItemVariantId is null && l.ItemId == v.ItemId)),
            Layer(locals.FirstOrDefault(l => l.ItemVariantId == v.ItemVariantId))));
    }

    private static WarehouseItemLayer? Layer(LocalRow? r) => r is null ? null
        : new WarehouseItemLayer(r.MinQty, r.MaxQty, r.ReorderQty, (ReplenishmentMethod?)r.ReplenishmentMethod, r.IsBlockedForIssue, r.IsBlockedForReceipt);

    private sealed record WarehouseRow(byte Kind, byte NegativeStockPolicy, byte DefaultValuationMethod);

    private sealed record VariantRow(int ItemVariantId, int ItemId, byte? ValuationMethod, byte? TrackingMode, decimal? MinQty, decimal? MaxQty,
        byte? ReplenishmentMethod, bool? IsBlockedForIssue);

    private sealed record LocalRow(int ItemId, int? ItemVariantId, decimal? MinQty, decimal? MaxQty, decimal? ReorderQty, byte? ReplenishmentMethod,
        bool IsBlockedForIssue, bool IsBlockedForReceipt);
}
