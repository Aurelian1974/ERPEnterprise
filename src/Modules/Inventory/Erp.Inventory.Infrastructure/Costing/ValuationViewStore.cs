using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Costing;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Inventory.Infrastructure.Movements;

namespace Erp.Inventory.Infrastructure.Costing;

/// <summary>Schimbarea metodei, straturile si ajustarile prin inv.usp_ValuationMethodChange_*, inv.usp_CostLayer_List, inv.usp_CostAdjustment_List (NET-06).</summary>
public sealed class ValuationViewStore(IDbSession session) : IValuationViewStore
{
    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public Task<InventorySaved> SaveChangeAsync(int companyId, int? id, ValuationMethod method, DateOnly effectiveDate, string reason, IReadOnlyList<int> itemIds,
        byte[]? rowVersion, int userId, CancellationToken ct)
    {
        var items = StockMovementStore.Table(("Id", typeof(int)));
        foreach (var item in itemIds)
        {
            items.Rows.Add(item);
        }

        return session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_ValuationMethodChange_Save", new
        {
            Id = id, CompanyId = companyId, NewMethod = (byte)method, EffectiveDate = effectiveDate.ToDateTime(TimeOnly.MinValue), Reason = reason,
            ItemIds = items.AsTableValuedParameter("inv.IdList"), RowVersion = rowVersion, UserId = userId,
        }, ct));
    }

    public async Task<ValuationChangeDto?> GetChangeAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_ValuationMethodChange_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var head = await grid.ReadSingleOrDefaultAsync<HeadRow>().ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }

        var items = (await grid.ReadAsync<(int ItemId, string ItemCode, string ItemName, byte OldMethod)>().ConfigureAwait(false))
            .Select(i => new ValuationChangeItemDto(i.ItemId, i.ItemCode, i.ItemName, (ValuationMethod)i.OldMethod)).ToList();
        var preview = (await grid.ReadAsync<PreviewRow>().ConfigureAwait(false))
            .Select(p => new ValuationChangePreviewDto(p.VariantCode, p.VariantName, p.WarehouseCode, p.DepotCode, Quantity.Format(p.Qty), p.OpenLayers,
                Money(p.Value), Quantity.Format(p.AverageCost is { } a ? decimal.Round(a, 4) : null))).ToList();
        return new ValuationChangeDto(head.Id, head.Number, (ValuationMethod)head.NewMethod, DateOnly.FromDateTime(head.EffectiveDate), head.Reason, head.Status,
            head.CreatedAt, head.CreatedByName, items, preview, head.RowVersion);
    }

    public async Task<IReadOnlyList<ValuationChangeListItemDto>> ListChangesAsync(int companyId, byte? status, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ListRow>(Command("inv.usp_ValuationMethodChange_List", new { CompanyId = companyId, Status = status }, ct))
            .ConfigureAwait(false))
        .Select(r => new ValuationChangeListItemDto(r.Id, r.Number, (ValuationMethod)r.NewMethod, DateOnly.FromDateTime(r.EffectiveDate), r.Status, r.ItemCount,
            r.FirstItemName))
        .ToList();

    public async Task<(InventorySaved Saved, IReadOnlyList<(int ItemId, ValuationMethod OldMethod)> Items, IReadOnlyList<CostKey> Keys)> ValidateChangeAsync(
        int companyId, int id, byte[] rowVersion, string number, DateOnly today, int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_ValuationMethodChange_Validate", new
        {
            CompanyId = companyId, Id = id, RowVersion = rowVersion, Number = number, Today = today.ToDateTime(TimeOnly.MinValue), UserId = userId,
        }, ct)).ConfigureAwait(false);
        var saved = await grid.ReadSingleAsync<InventorySaved>().ConfigureAwait(false);
        var items = (await grid.ReadAsync<(int ItemId, byte OldMethod)>().ConfigureAwait(false)).Select(i => (i.ItemId, (ValuationMethod)i.OldMethod)).ToList();
        var keys = (await grid.ReadAsync<(int WarehouseId, int? DepotId, int ItemVariantId, int? LotId)>().ConfigureAwait(false))
            .Select(k => new CostKey(k.WarehouseId, k.DepotId, k.ItemVariantId, k.LotId)).ToList();
        return (saved, items, keys);
    }

    public Task<InventorySaved> CancelChangeAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_ValuationMethodChange_Cancel",
            new { CompanyId = companyId, Id = id, RowVersion = rowVersion, UserId = userId }, ct));

    public async Task<CostLayersDto> LayersAsync(int companyId, int itemVariantId, int? warehouseId, bool openOnly, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_CostLayer_List",
            new { CompanyId = companyId, ItemVariantId = itemVariantId, WarehouseId = warehouseId, OpenOnly = openOnly }, ct)).ConfigureAwait(false);
        var layers = (await grid.ReadAsync<LayerRow>().ConfigureAwait(false))
            .Select(l => new CostLayerDto(l.Id, l.WarehouseId, l.WarehouseCode, l.DepotCode, l.LotId, l.SourceDocument, l.OriginDocument,
                DateOnly.FromDateTime(l.LayerDate), Quantity.Format(l.QtyIn), Quantity.Format(l.QtyRemaining), Quantity.Format(l.UnitCost), Money(l.ValueRemaining),
                l.IsNegative)).ToList();
        var consumptions = (await grid.ReadAsync<ConsumptionRow>().ConfigureAwait(false))
            .Select(c => new CostConsumptionDto(DateOnly.FromDateTime(c.MovementDate), c.Outbound, Quantity.Format(c.Qty), c.LayerDocument,
                Quantity.Format(c.UnitCost), Money(c.Value))).ToList();
        return new CostLayersDto(layers, consumptions);
    }

    public async Task<IReadOnlyList<CostAdjustmentDto>> AdjustmentsAsync(int companyId, byte? kind, int top, CancellationToken ct) =>
        (await session.Connection.QueryAsync<AdjustmentRow>(Command("inv.usp_CostAdjustment_List", new { CompanyId = companyId, Kind = kind, Top = top }, ct))
            .ConfigureAwait(false))
        .Select(a => new CostAdjustmentDto(a.Id, a.Kind, DateOnly.FromDateTime(a.AccountingDate), a.WarehouseCode, a.VariantCode, a.MovementId, a.SourceDocumentType,
            a.SourceDocumentId, Money(a.Amount), a.InStock, a.CreatedAt))
        .ToList();

    private sealed record HeadRow(int Id, string? Number, byte NewMethod, DateTime EffectiveDate, string Reason, byte Status, DateTime CreatedAt,
        string? CreatedByName, byte[] RowVersion);

    private sealed record PreviewRow(string VariantCode, string VariantName, string WarehouseCode, string? DepotCode, decimal Qty, int OpenLayers, decimal Value,
        decimal? AverageCost);

    private sealed record ListRow(int Id, string? Number, byte NewMethod, DateTime EffectiveDate, byte Status, int ItemCount, string? FirstItemName);

    private sealed record LayerRow(long Id, int WarehouseId, string WarehouseCode, string? DepotCode, int? LotId, string SourceDocument, string? OriginDocument,
        DateTime LayerDate, decimal QtyIn, decimal QtyRemaining, decimal UnitCost, decimal ValueRemaining, bool IsNegative);

    private sealed record ConsumptionRow(DateTime MovementDate, string Outbound, decimal Qty, string LayerDocument, decimal UnitCost, decimal Value);

    private sealed record AdjustmentRow(long Id, byte Kind, DateTime AccountingDate, string WarehouseCode, string VariantCode, long? MovementId,
        string? SourceDocumentType, long? SourceDocumentId, decimal Amount, bool InStock, DateTime CreatedAt);
}
