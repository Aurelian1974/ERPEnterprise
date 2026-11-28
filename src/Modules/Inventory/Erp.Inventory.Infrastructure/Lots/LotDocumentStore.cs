using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Lots;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Inventory.Infrastructure.Movements;

namespace Erp.Inventory.Infrastructure.Lots;

/// <summary>Schimbarea lotului, retragerea si trasabilitatea prin inv.usp_LotChange_*, inv.usp_Recall_*, inv.usp_Lot_Trace (NET-06).</summary>
public sealed class LotDocumentStore(IDbSession session) : ILotDocumentStore
{
    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private static DateTime? Date(DateOnly? date) => date?.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? Date(DateTime? date) => date is { } d ? DateOnly.FromDateTime(d) : null;

    public Task<InventorySaved> SaveLotChangeAsync(int companyId, int? id, int warehouseId, int? depotId, DateOnly date, string? notes,
        IReadOnlyList<(int ItemVariantId, int FromLotId, int? ToLotId, string? NewLotCode, DateOnly? NewExpiryDate, decimal Qty)> lines, byte[]? rowVersion,
        int userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var table = StockMovementStore.Table(("LineNumber", typeof(int)), ("ItemVariantId", typeof(int)), ("FromLotId", typeof(int)), ("ToLotId", typeof(int)),
            ("NewLotCode", typeof(string)), ("NewExpiryDate", typeof(DateTime)), ("QtyBase", typeof(decimal)));
        var number = 0;
        foreach (var l in lines)
        {
            table.Rows.Add(++number, l.ItemVariantId, l.FromLotId, (object?)l.ToLotId ?? DBNull.Value, (object?)l.NewLotCode ?? DBNull.Value,
                (object?)Date(l.NewExpiryDate) ?? DBNull.Value, l.Qty);
        }

        return session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_LotChange_Save", new
        {
            Id = id, CompanyId = companyId, WarehouseId = warehouseId, DepotId = depotId, DocumentDate = date.ToDateTime(TimeOnly.MinValue), Notes = notes,
            Lines = table.AsTableValuedParameter("inv.tt_LotChangeLine"), RowVersion = rowVersion, UserId = userId,
        }, ct));
    }

    public async Task<LotChangeDto?> GetLotChangeAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_LotChange_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var head = await grid.ReadSingleOrDefaultAsync<LotChangeRow>().ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LotChangeLineRow>().ConfigureAwait(false))
            .Select(l => new LotChangeLineDto(l.LineId, l.LineNumber, l.ItemVariantId, l.VariantCode, l.FromLotId, l.FromCku, l.ToLotId, l.ToCku, l.NewLotCode,
                Date(l.NewExpiryDate), Quantity.Format(l.QtyBase)))
            .ToList();
        return new LotChangeDto(head.Id, head.Number, head.WarehouseId, head.WarehouseCode, head.DepotId, DateOnly.FromDateTime(head.DocumentDate), head.Status,
            head.Notes, lines, head.RowVersion);
    }

    public async Task<IReadOnlyList<LotChangeItemDto>> ListLotChangesAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<LotChangeItemRow>(Command("inv.usp_LotChange_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(r => new LotChangeItemDto(r.Id, r.Number, r.WarehouseId, r.WarehouseCode, DateOnly.FromDateTime(r.DocumentDate), r.Status, r.LineCount))
        .ToList();

    public Task<InventorySaved> SetLotChangeStatusAsync(int companyId, int id, byte status, string? number, byte[] rowVersion, bool check, int userId,
        CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_LotChange_SetStatus", new
        {
            CompanyId = companyId, Id = id, Status = status, Number = number, RowVersion = rowVersion, Check = check, UserId = userId,
        }, ct));

    public Task<InventorySaved> SaveRecallAsync(int companyId, int? id, DateOnly date, byte sourceKind, string reason, int? supplierPartnerId,
        string? supplierLotCode, IReadOnlyList<int> lotIds, byte[]? rowVersion, int userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lotIds);
        var table = StockMovementStore.Table(("Id", typeof(int)));
        foreach (var lot in lotIds.Distinct())
        {
            table.Rows.Add(lot);
        }

        return session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_Recall_Save", new
        {
            Id = id, CompanyId = companyId, DocumentDate = date.ToDateTime(TimeOnly.MinValue), SourceKind = sourceKind, Reason = reason,
            SupplierPartnerId = supplierPartnerId, SupplierLotCode = supplierLotCode, LotIds = table.AsTableValuedParameter("inv.IdList"), RowVersion = rowVersion,
            UserId = userId,
        }, ct));
    }

    public async Task<RecallDto?> GetRecallAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_Recall_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var head = await grid.ReadSingleOrDefaultAsync<RecallRow>().ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }

        var lots = (await grid.ReadAsync<(int LotId, string CkuCode, string LotCode, string? SupplierLotCode, byte Status, decimal QtyOnHand)>()
                .ConfigureAwait(false))
            .Select(l => new RecallLotDto(l.LotId, l.CkuCode, l.LotCode, l.SupplierLotCode, (LotStatus)l.Status, Quantity.Format(l.QtyOnHand)))
            .ToList();
        var customers = (await grid.ReadAsync<RecallCustomerRow>().ConfigureAwait(false))
            .Select(c => new RecallCustomerDto(c.Id, c.LotId, c.CkuCode, c.PartnerId, c.PartnerName, c.SourceDocumentType, c.SourceDocumentId,
                c.SourceDocumentNumber, DateOnly.FromDateTime(c.MovementDate), Quantity.Format(c.QtyBase), c.Outcome, c.OutcomeNote))
            .ToList();
        return new RecallDto(head.Id, head.Number, DateOnly.FromDateTime(head.DocumentDate), head.SourceKind, head.Reason, head.SupplierPartnerId,
            head.SupplierName, head.SupplierLotCode, head.Status, head.ValidatedAt, head.ClosedAt, lots, customers, head.RowVersion);
    }

    public async Task<IReadOnlyList<RecallItemDto>> ListRecallsAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<RecallItemRow>(Command("inv.usp_Recall_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(r => new RecallItemDto(r.Id, r.Number, DateOnly.FromDateTime(r.DocumentDate), r.SourceKind, r.SupplierLotCode, r.Status, r.LotCount,
            r.CustomerCount))
        .ToList();

    public async Task<(InventorySaved Saved, IReadOnlyList<(int LotId, LotStatus Status, int? SupplierPartnerId)> Lots, int Customers)> ValidateRecallAsync(
        int companyId, int id, byte[] rowVersion, string number, int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_Recall_Validate", new
        {
            CompanyId = companyId, Id = id, RowVersion = rowVersion, Number = number, UserId = userId,
        }, ct)).ConfigureAwait(false);
        var saved = await grid.ReadSingleAsync<InventorySaved>().ConfigureAwait(false);
        var lots = (await grid.ReadAsync<(int LotId, byte Status, int? SupplierPartnerId)>().ConfigureAwait(false))
            .Select(l => (l.LotId, (LotStatus)l.Status, l.SupplierPartnerId)).ToList();
        var customers = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        return (saved, lots, customers);
    }

    public Task<InventorySaved> CloseRecallAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_Recall_Close", new
        {
            CompanyId = companyId, Id = id, RowVersion = rowVersion, UserId = userId,
        }, ct));

    public Task SetRecallOutcomeAsync(int companyId, long customerLineId, byte outcome, string? note, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("inv.usp_RecallCustomer_SetOutcome", new
        {
            CompanyId = companyId, Id = customerLineId, Outcome = outcome, Note = note, UserId = userId,
        }, ct));

    public async Task<LotTraceDto?> TraceAsync(int companyId, int lotId, int maxDepth, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_Lot_Trace", new { CompanyId = companyId, LotId = lotId, MaxDepth = maxDepth },
            ct)).ConfigureAwait(false);
        var head = await grid.ReadSingleOrDefaultAsync<TraceHeadRow>().ConfigureAwait(false);
        if (head is null)
        {
            return null;
        }

        var back = (await grid.ReadAsync<(int Depth, int LotId, string CkuCode, string? SupplierLotCode, string? SupplierName, DateTime? FirstReceiptDate)>()
                .ConfigureAwait(false))
            .Select(b => new LotTraceOriginDto(b.Depth, b.LotId, b.CkuCode, b.SupplierLotCode, b.SupplierName, Date(b.FirstReceiptDate)))
            .ToList();
        var forward = (await grid.ReadAsync<TraceMovementRow>().ConfigureAwait(false))
            .Select(m => new LotTraceMovementDto(m.Depth, m.MovementId, m.LotId, m.CkuCode, DateOnly.FromDateTime(m.MovementDate), m.MovementTypeCode,
                m.WarehouseCode, Quantity.Format(m.QtyBase), m.PartnerId, m.PartnerName, m.SourceDocumentType, m.SourceDocumentId, m.SourceDocumentNumber))
            .ToList();
        var stock = (await grid.ReadAsync<(int WarehouseId, string WarehouseCode, byte WarehouseKind, decimal QtyOnHand)>().ConfigureAwait(false))
            .Select(s => new LotStockDto(s.WarehouseId, s.WarehouseCode, Quantity.Format(s.QtyOnHand)))
            .ToList();
        return new LotTraceDto(head.LotId, head.CkuCode, head.SupplierLotCode, head.SupplierName, Date(head.FirstReceiptDate),
            head.ReceiptDocumentType is null ? null : $"{head.ReceiptDocumentType} {head.ReceiptDocumentNumber}".Trim(), back, forward, stock);
    }

    private sealed record LotChangeRow(int Id, string? Number, int WarehouseId, string WarehouseCode, int? DepotId, DateTime DocumentDate, byte Status,
        string? Notes, byte[] RowVersion);

    private sealed record LotChangeLineRow(long LineId, int LineNumber, int ItemVariantId, string VariantCode, int FromLotId, string FromCku, int? ToLotId,
        string? ToCku, string? NewLotCode, DateTime? NewExpiryDate, decimal QtyBase);

    private sealed record LotChangeItemRow(int Id, string? Number, int WarehouseId, string WarehouseCode, DateTime DocumentDate, byte Status, int LineCount);

    private sealed record RecallRow(int Id, string? Number, DateTime DocumentDate, byte SourceKind, string Reason, int? SupplierPartnerId, string? SupplierName,
        string? SupplierLotCode, byte Status, DateTime? ValidatedAt, DateTime? ClosedAt, byte[] RowVersion);

    private sealed record RecallCustomerRow(long Id, int LotId, string CkuCode, int? PartnerId, string? PartnerName, string SourceDocumentType,
        long SourceDocumentId, string? SourceDocumentNumber, DateTime MovementDate, decimal QtyBase, byte? Outcome, string? OutcomeNote);

    private sealed record RecallItemRow(int Id, string? Number, DateTime DocumentDate, byte SourceKind, string? SupplierLotCode, byte Status, int LotCount,
        int CustomerCount);

    private sealed record TraceHeadRow(int LotId, string CkuCode, string LotCode, string? SupplierLotCode, DateTime? ExpiryDate, byte Status,
        int? SupplierPartnerId, string? SupplierName, DateTime? FirstReceiptDate, string? ReceiptDocumentType, string? ReceiptDocumentNumber,
        decimal? ReceiptQty);

    private sealed record TraceMovementRow(int Depth, long MovementId, int LotId, string CkuCode, DateTime MovementDate, string MovementTypeCode,
        string WarehouseCode, decimal QtyBase, int? PartnerId, string? PartnerName, string SourceDocumentType, long SourceDocumentId,
        string? SourceDocumentNumber);
}
