using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Documents;
using Erp.Inventory.Domain;

namespace Erp.Inventory.Infrastructure.Documents;

/// <summary>Documentele de stoc prin procedurile inv.usp_StockDocument_* (NET-06); cantitatile si valorile ies ca text (UI, bani).</summary>
public sealed class StockDocumentStore(IDbSession session) : IStockDocumentStore
{
    public async Task<IReadOnlyList<StockDocumentItemDto>> ListAsync(int companyId, string? documentType, byte? status, int? warehouseId, DateOnly? from,
        DateOnly? to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(Command("inv.usp_StockDocument_List", new
        {
            CompanyId = companyId, DocumentType = documentType, Status = status, WarehouseId = warehouseId,
            DateFrom = from?.ToDateTime(TimeOnly.MinValue), DateTo = to?.ToDateTime(TimeOnly.MinValue),
        }, ct)).ConfigureAwait(false))
        .Select(r => new StockDocumentItemDto(r.Id, r.DocumentType, r.Number, DateOnly.FromDateTime(r.DocumentDate), r.Status, r.ApprovalStatus, r.IsReversal,
            r.FromWarehouseId, r.FromWarehouseCode, r.ToWarehouseId, r.ToWarehouseCode, r.ReasonName, Money(r.TotalCost), r.LineCount)
        {
            WarehouseDimensions = Dimensions(r.FromWarehouseDimensionId, r.ToWarehouseDimensionId),
        })
        .ToList();

    public async Task<StockDocumentDto?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("inv.usp_StockDocument_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var d = await grid.ReadSingleOrDefaultAsync<HeaderRow>().ConfigureAwait(false);
        if (d is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).Select(l => new StockDocumentLineDto(l.Id, l.LineNumber, l.ItemVariantId, l.VariantCode,
            l.ItemNameSnapshot, l.UomId, Quantity.Format(l.Qty), Quantity.Format(l.UomFactor), Quantity.Format(l.QtyBase), Quantity.Format(l.UnitCost),
            l.FromDepotId, l.FromWarehouseId, l.ToDepotId, l.ToWarehouseId, l.CostCenterId, l.ProjectId, Quantity.Format(l.ReceivedQtyBase),
            Quantity.Format(l.DifferenceQtyBase), l.DifferenceResolution, l.ReversedLineId, Quantity.Format(l.UnitCostPosted), Money(l.CostAmountPosted)))
            .ToList();
        return new StockDocumentDto(d.Id, d.DocumentType, d.Number, DateOnly.FromDateTime(d.DocumentDate), DateOnly.FromDateTime(d.AccountingDate), d.FromDepotId,
            d.FromWarehouseId, d.FromWarehouseCode, d.ToDepotId, d.ToWarehouseId, d.ToWarehouseCode, d.ReasonId, d.ReasonCode, d.ReasonName,
            d.RequiresAttachment ?? false, d.PartnerId, d.CostCenterId, d.ProjectId, d.EmployeeId, d.TransferMode, d.TransitShipmentId, d.RelatedDocumentId,
            d.IsReversal, d.Status, d.ApprovalStatus, d.ApprovalNote, d.Notes, d.IsForcedNegative, Money(d.TotalCost), d.ValidatedAt, d.ReversedByDocumentId,
            d.CreatedBy, d.CreatedAt, d.ModifiedAt, lines, d.RowVersion)
        {
            WarehouseDimensions = Dimensions(d.FromWarehouseDimensionId, d.ToWarehouseDimensionId),
        };
    }

    public Task<InventorySaved> SaveAsync(StockDocumentRecord d, int userId, CancellationToken ct)
    {
        var lines = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
        {
            ("LineNumber", typeof(int)), ("ItemVariantId", typeof(int)), ("UomId", typeof(int)), ("Qty", typeof(decimal)), ("UomFactor", typeof(decimal)),
            ("UnitCost", typeof(decimal)), ("FromDepotId", typeof(int)), ("FromWarehouseId", typeof(int)), ("ToDepotId", typeof(int)),
            ("ToWarehouseId", typeof(int)), ("CostCenterId", typeof(int)), ("ProjectId", typeof(int)),
        })
        {
            lines.Columns.Add(name, type);
        }

        foreach (var l in d.Lines)
        {
            lines.Rows.Add(l.LineNo, l.ItemVariantId, (object?)l.UomId ?? DBNull.Value, l.Qty, l.UomFactor, (object?)l.UnitCost ?? DBNull.Value,
                (object?)l.FromDepotId ?? DBNull.Value, (object?)l.FromWarehouseId ?? DBNull.Value, (object?)l.ToDepotId ?? DBNull.Value,
                (object?)l.ToWarehouseId ?? DBNull.Value, (object?)l.CostCenterId ?? DBNull.Value, (object?)l.ProjectId ?? DBNull.Value);
        }

        return session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_StockDocument_Save", new
        {
            d.Id, d.CompanyId, d.DocumentType, DocumentDate = d.DocumentDate.ToDateTime(TimeOnly.MinValue),
            AccountingDate = d.AccountingDate.ToDateTime(TimeOnly.MinValue), d.FromDepotId, d.FromWarehouseId, d.ToDepotId, d.ToWarehouseId, d.ReasonId,
            d.PartnerId, d.CostCenterId, d.ProjectId, d.EmployeeId, d.Notes, Lines = lines.AsTableValuedParameter("inv.tt_StockDocumentLine"), d.RowVersion,
            UserId = userId,
        }, ct));
    }

    public Task BeginValidationAsync(int companyId, int id, byte[] rowVersion, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("inv.usp_StockDocument_BeginValidation", new { CompanyId = companyId, Id = id, RowVersion = rowVersion }, ct));

    public Task<InventorySaved> MarkValidatedAsync(int companyId, int id, string number, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_StockDocument_MarkValidated",
            new { CompanyId = companyId, Id = id, Number = number, UserId = userId }, ct));

    public Task<int> CreateReversalAsync(int companyId, int id, DateOnly documentDate, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("inv.usp_StockDocument_CreateReversal", new
        {
            CompanyId = companyId, Id = id, DocumentDate = documentDate.ToDateTime(TimeOnly.MinValue), RowVersion = rowVersion, UserId = userId,
        }, ct));

    public Task MarkReversedAsync(int companyId, int id, int reversalId, string number, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("inv.usp_StockDocument_MarkReversed",
            new { CompanyId = companyId, Id = id, ReversalId = reversalId, Number = number, UserId = userId }, ct));

    public Task<InventorySaved> CancelAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_StockDocument_Cancel",
            new { CompanyId = companyId, Id = id, RowVersion = rowVersion, UserId = userId }, ct));

    public Task<InventorySaved> ApproveAsync(int companyId, int id, bool approve, string? note, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<InventorySaved>(Command("inv.usp_StockDocument_Approve",
            new { CompanyId = companyId, Id = id, Approve = approve, Note = note, RowVersion = rowVersion, UserId = userId }, ct));

    public async Task<IReadOnlyList<MovementReasonDto>> ReasonsAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ReasonRow>(Command("inv.usp_MovementReason_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(r => new MovementReasonDto(r.Id, r.Code, r.Name, r.Direction, r.VatTreatment, r.IsDeductible, r.RequiresAttachment, Money(r.ApprovalThreshold),
            r.ApproverRoleCode, r.NoPosting, r.CostSource, r.IsSystem, r.IsActive))
        .ToList();

    private static string? Money(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture);

    private static IReadOnlyList<int> Dimensions(int? from, int? to) => new[] { from, to }.OfType<int>().ToList();

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record ItemRow(int Id, string DocumentType, string? Number, DateTime DocumentDate, byte Status, byte ApprovalStatus, bool IsReversal,
        int? FromWarehouseId, string? FromWarehouseCode, int? FromWarehouseDimensionId, int? ToWarehouseId, string? ToWarehouseCode,
        int? ToWarehouseDimensionId, string? ReasonName, decimal? TotalCost, int LineCount);

    private sealed record HeaderRow(int Id, string DocumentType, string? Number, DateTime DocumentDate, DateTime AccountingDate, int? FromDepotId,
        int? FromWarehouseId, string? FromWarehouseCode, int? FromWarehouseDimensionId, int? ToDepotId, int? ToWarehouseId, string? ToWarehouseCode,
        int? ToWarehouseDimensionId, int? ReasonId, string? ReasonCode, string? ReasonName, bool? RequiresAttachment, int? PartnerId, int? CostCenterId,
        int? ProjectId, int? EmployeeId, byte? TransferMode, int? TransitShipmentId, int? RelatedDocumentId, bool IsReversal, byte Status, byte ApprovalStatus,
        string? ApprovalNote, string? Notes, bool IsForcedNegative, decimal? TotalCost, DateTime? ValidatedAt, int? ReversedByDocumentId, int CreatedBy,
        DateTime CreatedAt, DateTime? ModifiedAt, byte[] RowVersion);

    private sealed record LineRow(long Id, int LineNumber, int ItemVariantId, string VariantCode, string ItemNameSnapshot, int? UomId, decimal Qty, decimal UomFactor,
        decimal QtyBase, decimal? UnitCost, int? FromDepotId, int? FromWarehouseId, int? ToDepotId, int? ToWarehouseId, int? CostCenterId, int? ProjectId,
        decimal? ReceivedQtyBase, decimal? DifferenceQtyBase, byte? DifferenceResolution, long? ReversedLineId, decimal? UnitCostPosted,
        decimal? CostAmountPosted);

    private sealed record ReasonRow(int Id, string Code, string Name, short Direction, byte VatTreatment, bool IsDeductible, bool RequiresAttachment,
        decimal? ApprovalThreshold, string? ApproverRoleCode, bool NoPosting, byte CostSource, bool IsSystem, bool IsActive);
}
