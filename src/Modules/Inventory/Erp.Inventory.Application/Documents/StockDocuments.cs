using Erp.Core.Contracts;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Domain;
using FluentValidation;

namespace Erp.Inventory.Application.Documents;

/// <summary>O linie de document de stoc; cantitatea in UM liniei si factorul spre UM de baza (Nucleu 3.3), costul doar unde se introduce.</summary>
public sealed record StockDocumentLineInput(int ItemVariantId, int? UomId, string Qty, string UomFactor, string? UnitCost, int? FromDepotId,
    int? FromWarehouseId, int? ToDepotId, int? ToWarehouseId, int? CostCenterId, int? ProjectId);

public sealed record StockDocumentLineDto(long Id, int LineNo, int ItemVariantId, string VariantCode, string ItemName, int? UomId, string Qty, string UomFactor,
    string QtyBase, string? UnitCost, int? FromDepotId, int? FromWarehouseId, int? ToDepotId, int? ToWarehouseId, int? CostCenterId, int? ProjectId,
    string? ReceivedQtyBase, string? DifferenceQtyBase, byte? DifferenceResolution, long? ReversedLineId, string? UnitCostPosted, string? CostAmountPosted,
    long? ShipmentLineId);

/// <summary>Documentul de stoc cu liniile lui (machetele s2-01…s2-04). Status: 1 Ciorna, 2 Validat, 3 Stornat, 4 Anulat.</summary>
public sealed record StockDocumentDto(int Id, string DocumentType, string? Number, DateOnly DocumentDate, DateOnly AccountingDate, int? FromDepotId,
    int? FromWarehouseId, string? FromWarehouseCode, int? ToDepotId, int? ToWarehouseId, string? ToWarehouseCode, int? ReasonId, string? ReasonCode,
    string? ReasonName, bool RequiresAttachment, int? PartnerId, int? CostCenterId, int? ProjectId, int? EmployeeId, byte? TransferMode,
    int? TransitShipmentId, int? RelatedDocumentId, bool IsReversal, byte Status, byte ApprovalStatus, string? ApprovalNote, string? Notes,
    bool IsForcedNegative, string? TotalCost, DateTime? ValidatedAt, int? ReversedByDocumentId, int CreatedBy, DateTime CreatedAt, DateTime? ModifiedAt,
    IReadOnlyList<StockDocumentLineDto> Lines, byte[] RowVersion)
{
    /// <summary>Valorile de dimensiune ale gestiunilor, pentru scope-ul pe gestiuni (nu se trimit in API).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<int> WarehouseDimensions { get; init; } = [];
}

public sealed record StockDocumentItemDto(int Id, string DocumentType, string? Number, DateOnly DocumentDate, byte Status, byte ApprovalStatus,
    bool IsReversal, int? FromWarehouseId, string? FromWarehouseCode, int? ToWarehouseId, string? ToWarehouseCode, string? ReasonName, string? TotalCost,
    int LineCount)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<int> WarehouseDimensions { get; init; } = [];
}

public sealed record MovementReasonDto(int Id, string Code, string Name, short Direction, byte VatTreatment, bool IsDeductible, bool RequiresAttachment,
    string? ApprovalThreshold, string? ApproverRoleCode, bool NoPosting, byte CostSource, bool IsSystem, bool IsActive);

public sealed record StockDocumentRecord(int? Id, int CompanyId, string DocumentType, DateOnly DocumentDate, DateOnly AccountingDate, int? FromDepotId,
    int? FromWarehouseId, int? ToDepotId, int? ToWarehouseId, int? ReasonId, int? PartnerId, int? CostCenterId, int? ProjectId, int? EmployeeId,
    string? Notes, IReadOnlyList<StockDocumentLineRecord> Lines, byte[]? RowVersion);

public sealed record StockDocumentLineRecord(int LineNo, int ItemVariantId, int? UomId, decimal Qty, decimal UomFactor, decimal? UnitCost, int? FromDepotId,
    int? FromWarehouseId, int? ToDepotId, int? ToWarehouseId, int? CostCenterId, int? ProjectId);

/// <summary>Accesul la date pentru documentele de stoc (inv.usp_StockDocument_*).</summary>
public interface IStockDocumentStore
{
    Task<IReadOnlyList<StockDocumentItemDto>> ListAsync(int companyId, string? documentType, byte? status, int? warehouseId, DateOnly? from, DateOnly? to,
        CancellationToken ct);

    Task<StockDocumentDto?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<InventorySaved> SaveAsync(StockDocumentRecord document, int userId, CancellationToken ct);

    Task BeginValidationAsync(int companyId, int id, byte[] rowVersion, CancellationToken ct);

    Task<InventorySaved> MarkValidatedAsync(int companyId, int id, string number, int userId, CancellationToken ct);

    Task<int> CreateReversalAsync(int companyId, int id, DateOnly documentDate, byte[] rowVersion, int userId, CancellationToken ct);

    Task MarkReversedAsync(int companyId, int id, int reversalId, string number, int userId, CancellationToken ct);

    Task<InventorySaved> CancelAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct);

    Task<InventorySaved> ApproveAsync(int companyId, int id, bool approve, string? note, byte[] rowVersion, int userId, CancellationToken ct);

    Task<IReadOnlyList<MovementReasonDto>> ReasonsAsync(int companyId, CancellationToken ct);

    Task<TransferPlan> PlanTransferAsync(int companyId, int documentId, CancellationToken ct);

    Task<int?> MarkShippedAsync(int companyId, int documentId, byte transferMode, int userId, CancellationToken ct);

    Task<int> CreateReceiptAsync(int companyId, int shipmentId, DateOnly documentDate, int userId, CancellationToken ct);

    Task<InventorySaved> SaveReceiptAsync(int companyId, int id, string? notes, IReadOnlyList<ReceiptLineRecord> lines, byte[] rowVersion, int userId,
        CancellationToken ct);

    Task<byte> RefreshShipmentAsync(int companyId, int shipmentId, int userId, CancellationToken ct);

    Task<IReadOnlyList<TransitShipmentDto>> ShipmentsAsync(int companyId, int? toWarehouseId, bool openOnly, CancellationToken ct);
}

/// <summary>Etapele unei note de predare (S2 3.4) si gestiunea de tranzit a firmei.</summary>
public sealed record TransferPlan(byte TransferMode, int TransitWarehouseId);

public sealed record ReceiptLineRecord(long LineId, decimal ReceivedQtyBase, byte? Resolution);

/// <summary>O expeditie in tranzit (S2 3.4). Status: 1 Trimis, 2 Primit partial, 3 Primit, 4 Primit cu diferente, 5 Inchis.</summary>
public sealed record TransitShipmentDto(int Id, int NpDocumentId, string? NpNumber, int FromWarehouseId, string FromWarehouseCode, int ToWarehouseId,
    string ToWarehouseCode, DateOnly ShippedDate, byte Status, int AgeDays)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<int> WarehouseDimensions { get; init; } = [];
}

/// <summary>Cantitatea primita pe o linie a notei de primire si rezolvarea diferentei (1 tranzit, 2 retur, 3 lipsa imputabila, 4 neimputabila, 5 plus).</summary>
public sealed record ReceiptLineInput(long LineId, string ReceivedQtyBase, byte? Resolution);

/// <summary>Documentele de stoc ale firmei curente, pe gestiunile permise (S1 3.7).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
public sealed record GetStockDocuments(string? DocumentType = null, byte? Status = null, int? WarehouseId = null, DateOnly? DateFrom = null,
    DateOnly? DateTo = null) : IQuery<IReadOnlyList<StockDocumentItemDto>>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
public sealed record GetStockDocument(int Id) : IQuery<StockDocumentDto>;

/// <summary>Motivele de incarcare si descarcare (S2 3.5).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
public sealed record GetMovementReasons : IQuery<IReadOnlyList<MovementReasonDto>>;

/// <summary>
/// Salveaza ciorna unui document de stoc (S2 3.3). Pipeline-ul cere consultarea documentelor; dreptul de editare al tipului (de exemplu
/// Inv.LoadUnload.Edit) si scope-ul pe gestiuni se verifica in handler.
/// </summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
[Audited("Inv.StockDocument.Save")]
public sealed record SaveStockDocument(int? Id, string DocumentType, DateOnly DocumentDate, DateOnly? AccountingDate, int? FromDepotId, int? FromWarehouseId,
    int? ToDepotId, int? ToWarehouseId, int? ReasonId, int? PartnerId, int? CostCenterId, int? ProjectId, int? EmployeeId, string? Notes,
    IReadOnlyList<StockDocumentLineInput> Lines, byte[]? RowVersion) : ICommand<InventorySaved>;

/// <summary>Valideaza documentul: numarul, miscarile prin portul de stoc, in aceeasi tranzactie (S2 4.1).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
[Audited("Inv.StockDocument.Validate")]
public sealed record ValidateStockDocument(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Storneaza un document validat: un document de acelasi tip, cu miscarile de semn opus la costul originalului (S2 4.6).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentReverse)]
[Audited("Inv.StockDocument.Reverse")]
public sealed record ReverseStockDocument(int Id, DateOnly? DocumentDate, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Expeditiile in tranzit spre gestiunile permise (S2 3.4, „De primit”).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
public sealed record GetTransitShipments(int? ToWarehouseId = null, bool OpenOnly = true) : IQuery<IReadOnlyList<TransitShipmentDto>>;

/// <summary>Creeaza nota de primire a unei expeditii, cu cantitatile ramase in tranzit (S2 4.3).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
[Audited("Inv.StockDocument.CreateReceipt")]
public sealed record CreateReceiptFromShipment(int ShipmentId, DateOnly DocumentDate) : ICommand<InventorySaved>;

/// <summary>Salveaza cantitatile primite si rezolvarea diferentelor pe nota de primire (S2 3.4).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
[Audited("Inv.StockDocument.SaveReceipt")]
public sealed record SaveReceipt(int Id, string? Notes, IReadOnlyList<ReceiptLineInput> Lines, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Anuleaza o ciorna.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.DocumentView)]
[Audited("Inv.StockDocument.Cancel")]
public sealed record CancelStockDocument(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Aproba sau respinge o descarcare peste prag (S2 3.6), patru ochi.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LoadUnloadApprove)]
[Audited("Inv.StockDocument.Approve")]
public sealed record ApproveStockDocument(int Id, bool Approve, string? Note, byte[] RowVersion) : ICommand<InventorySaved>;

internal sealed class SaveStockDocumentValidator : AbstractValidator<SaveStockDocument>
{
    public SaveStockDocumentValidator()
    {
        RuleFor(x => x.DocumentType).Must(t => StockDocumentTypes.IsSimple(t) || t == StockDocumentTypes.TransferNote)
            .WithMessage("Tipul documentului nu e cunoscut.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Documentul are cel puțin o linie.");
        RuleForEach(x => x.Lines).Must(l => Quantity.TryParse(l.Qty, out var q) && q > 0).WithMessage("Cantitatea este pozitivă, cu cel mult șase zecimale.");
        RuleForEach(x => x.Lines).Must(l => Quantity.TryParse(l.UomFactor, out var f) && f > 0).WithMessage("Factorul UM este pozitiv.");
        RuleForEach(x => x.Lines).Must(l => string.IsNullOrWhiteSpace(l.UnitCost) || (Quantity.TryParse(l.UnitCost, out var c) && c >= 0))
            .WithMessage("Costul unitar este o valoare pozitivă, cu cel mult șase zecimale.");
        RuleFor(x => x.Notes).MaximumLength(1000).WithMessage("Observațiile au cel mult 1000 de caractere.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class ApproveStockDocumentValidator : AbstractValidator<ApproveStockDocument>
{
    public ApproveStockDocumentValidator()
    {
        RuleFor(x => x.Note).NotEmpty().When(x => !x.Approve).WithMessage("Respingerea cere motivul.").MaximumLength(500)
            .WithMessage("Motivul are cel mult 500 de caractere.");
    }
}

/// <summary>Drepturile pe documente: permisiunea tipului si scope-ul pe gestiunile documentului (S1 3.7, S1-A5).</summary>
internal static class DocumentAccess
{
    public static ErpException NotFound() => ErpException.NotFound("INV_DOCUMENT_NOT_FOUND", "Documentul de stoc nu există în firma curentă.");

    public static bool CanSee(ICurrentContext context, IEnumerable<int> warehouseDimensions) =>
        warehouseDimensions.All(d => context.HasPermission(InventoryPermissions.DocumentView, new DimensionRef(DimensionTypes.Warehouse, d)));

    public static void Require(ICurrentContext context, string permission, IEnumerable<int> warehouseDimensions)
    {
        if (!context.HasPermission(permission) || !warehouseDimensions.All(d => context.HasPermission(permission, new DimensionRef(DimensionTypes.Warehouse, d))))
        {
            throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, "Nu aveți dreptul pe acest document sau pe gestiunile lui.");
        }
    }

    public static StockDocumentKind Kind(string code) =>
        StockDocumentTypes.Find(code) ?? throw new ErpException(ErpErrorKind.Validation, "INV_DOCUMENT_TYPE_INVALID", "Tipul documentului nu e cunoscut.");
}

internal sealed class GetStockDocumentsHandler(IStockDocumentStore store, ICurrentContext context)
    : IQueryHandler<GetStockDocuments, IReadOnlyList<StockDocumentItemDto>>
{
    public async Task<IReadOnlyList<StockDocumentItemDto>> Handle(GetStockDocuments request, CancellationToken cancellationToken) =>
        (await store.ListAsync(context.CompanyId, request.DocumentType, request.Status, request.WarehouseId, request.DateFrom, request.DateTo,
            cancellationToken).ConfigureAwait(false)).Where(d => DocumentAccess.CanSee(context, d.WarehouseDimensions)).ToList();
}

internal sealed class GetStockDocumentHandler(IStockDocumentStore store, ICurrentContext context) : IQueryHandler<GetStockDocument, StockDocumentDto>
{
    public async Task<StockDocumentDto> Handle(GetStockDocument request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) is { } d && DocumentAccess.CanSee(context, d.WarehouseDimensions)
            ? d : throw DocumentAccess.NotFound();
}

internal sealed class GetMovementReasonsHandler(IStockDocumentStore store, ICurrentContext context) : IQueryHandler<GetMovementReasons, IReadOnlyList<MovementReasonDto>>
{
    public Task<IReadOnlyList<MovementReasonDto>> Handle(GetMovementReasons request, CancellationToken cancellationToken) =>
        store.ReasonsAsync(context.CompanyId, cancellationToken);
}

internal sealed class SaveStockDocumentHandler(IStockDocumentStore store, IWarehouseStore warehouses, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveStockDocument, InventorySaved>
{
    public async Task<InventorySaved> Handle(SaveStockDocument request, CancellationToken cancellationToken)
    {
        var kind = DocumentAccess.Kind(request.DocumentType);
        var transfer = request.DocumentType == StockDocumentTypes.TransferNote;
        var from = kind.Direction < 0 || transfer ? request.FromWarehouseId : null;
        var to = kind.Direction > 0 || transfer ? request.ToWarehouseId : null;
        var dimensions = new List<int>();
        int? costCenter = request.CostCenterId;
        foreach (var id in new[] { from, to }.OfType<int>())
        {
            var warehouse = await warehouses.GetAsync(context.CompanyId, id, cancellationToken).ConfigureAwait(false) ?? throw WarehouseAccess.NotFound();
            dimensions.Add(warehouse.DimensionValueId);
            // Bonul de consum propune centrul de cost al gestiunii (S2 3.7).
            costCenter ??= request.DocumentType == StockDocumentTypes.Consumption ? warehouse.DefaultCostCenterId : null;
        }

        DocumentAccess.Require(context, kind.EditPermission, dimensions);
        if (request.Id is { } existing && await store.GetAsync(context.CompanyId, existing, cancellationToken).ConfigureAwait(false) is { } current)
        {
            DocumentAccess.Require(context, kind.EditPermission, current.WarehouseDimensions);
        }

        var lines = request.Lines.Select((l, i) => new StockDocumentLineRecord(i + 1, l.ItemVariantId, l.UomId, Quantity.ParseOptional(l.Qty) ?? 0,
            Quantity.ParseOptional(l.UomFactor) ?? 1, Quantity.ParseOptional(l.UnitCost), l.FromDepotId, l.FromWarehouseId, l.ToDepotId, l.ToWarehouseId,
            l.CostCenterId, l.ProjectId)).ToList();
        var record = new StockDocumentRecord(request.Id, context.CompanyId, request.DocumentType, request.DocumentDate, request.AccountingDate ?? request.DocumentDate,
            request.FromDepotId, from, request.ToDepotId, to, request.ReasonId, request.PartnerId, costCenter, request.ProjectId, request.EmployeeId,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(), lines, request.RowVersion);
        var saved = await store.SaveAsync(record, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.StockDocument", saved.Id, new { record.DocumentType, record.DocumentDate, record.FromWarehouseId, record.ToWarehouseId, record.ReasonId, Lines = lines.Count });
        return saved;
    }
}

internal sealed class ValidateStockDocumentHandler(IStockDocumentStore store, StockLedger ledger, INumberSeriesService numbers, ICurrentContext context, TimeProvider time,
    TransferService transfers, IAuditScope audit) : ICommandHandler<ValidateStockDocument, InventorySaved>, ICommandHandler<ReverseStockDocument, InventorySaved>,
    ICommandHandler<CancelStockDocument, InventorySaved>, ICommandHandler<ApproveStockDocument, InventorySaved>
{
    public async Task<InventorySaved> Handle(ValidateStockDocument request, CancellationToken cancellationToken)
    {
        var document = await Load(request.Id, cancellationToken).ConfigureAwait(false);
        var kind = DocumentAccess.Kind(document.DocumentType);
        if (document.DocumentType == StockDocumentTypes.TransferNote)
        {
            return await transfers.ValidateTransferAsync(document, kind, request.RowVersion, cancellationToken).ConfigureAwait(false);
        }

        if (document.DocumentType == StockDocumentTypes.ReceiptNote)
        {
            return await transfers.ValidateReceiptAsync(document, kind, request.RowVersion, cancellationToken).ConfigureAwait(false);
        }

        DocumentAccess.Require(context, kind.ValidatePermission, document.WarehouseDimensions);
        await store.BeginValidationAsync(context.CompanyId, request.Id, request.RowVersion, cancellationToken).ConfigureAwait(false);
        var number = await numbers.NextAsync(context.CompanyId, kind.NumberSeriesType, document.DocumentDate, cancellationToken).ConfigureAwait(false);
        var lines = document.Lines.Select(l => new StockMovementRequest(l.Id, 1, kind.MovementTypeCode,
            kind.Direction > 0 ? l.ToDepotId ?? document.ToDepotId : l.FromDepotId ?? document.FromDepotId,
            (kind.Direction > 0 ? l.ToWarehouseId ?? document.ToWarehouseId : l.FromWarehouseId ?? document.FromWarehouseId) ?? 0, l.ItemVariantId,
            kind.Direction * (Quantity.ParseOptional(l.QtyBase) ?? 0), kind.Direction > 0 ? Quantity.ParseOptional(l.UnitCost) : null, null, null, null, null,
            l.CostCenterId ?? document.CostCenterId, l.ProjectId ?? document.ProjectId, document.ReasonId)).ToList();
        await ledger.PostWithOutcomeAsync(new StockPostingRequest(context.CompanyId, "inv", document.DocumentType, document.Id, number.Formatted,
            document.DocumentDate, document.AccountingDate, document.PartnerId, false, lines), document.EmployeeId, cancellationToken).ConfigureAwait(false);
        var saved = await store.MarkValidatedAsync(context.CompanyId, request.Id, number.Formatted, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.StockDocument", request.Id, new { Number = number.Formatted, Validated = true });
        return saved;
    }

    public async Task<InventorySaved> Handle(ReverseStockDocument request, CancellationToken cancellationToken)
    {
        var document = await Load(request.Id, cancellationToken).ConfigureAwait(false);
        var kind = DocumentAccess.Kind(document.DocumentType);
        DocumentAccess.Require(context, InventoryPermissions.DocumentReverse, document.WarehouseDimensions);
        var date = request.DocumentDate ?? BusinessDay.Today(time);
        var reversalId = await store.CreateReversalAsync(context.CompanyId, request.Id, date, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        var number = await numbers.NextAsync(context.CompanyId, kind.NumberSeriesType, date, cancellationToken).ConfigureAwait(false);
        await ledger.ReverseWithOutcomeAsync(new StockReversalRequest(context.CompanyId, "inv", request.Id, null, date, document.DocumentType, reversalId,
            number.Formatted), cancellationToken).ConfigureAwait(false);
        await store.MarkReversedAsync(context.CompanyId, request.Id, reversalId, number.Formatted, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.StockDocument", request.Id, new { ReversedBy = reversalId, Number = number.Formatted });
        var reversal = await store.GetAsync(context.CompanyId, reversalId, cancellationToken).ConfigureAwait(false) ?? throw DocumentAccess.NotFound();
        return new InventorySaved(reversalId, reversal.RowVersion);
    }

    public async Task<InventorySaved> Handle(CancelStockDocument request, CancellationToken cancellationToken)
    {
        var document = await Load(request.Id, cancellationToken).ConfigureAwait(false);
        DocumentAccess.Require(context, DocumentAccess.Kind(document.DocumentType).EditPermission, document.WarehouseDimensions);
        var saved = await store.CancelAsync(context.CompanyId, request.Id, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.StockDocument", request.Id, new { Cancelled = true });
        return saved;
    }

    public async Task<InventorySaved> Handle(ApproveStockDocument request, CancellationToken cancellationToken)
    {
        var document = await Load(request.Id, cancellationToken).ConfigureAwait(false);
        DocumentAccess.Require(context, InventoryPermissions.LoadUnloadApprove, document.WarehouseDimensions);
        var saved = await store.ApproveAsync(context.CompanyId, request.Id, request.Approve, request.Note?.Trim(), request.RowVersion, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.StockDocument", request.Id, new { request.Approve, request.Note });
        return saved;
    }

    private async Task<StockDocumentDto> Load(int id, CancellationToken ct) =>
        await store.GetAsync(context.CompanyId, id, ct).ConfigureAwait(false) is { } d && DocumentAccess.CanSee(context, d.WarehouseDimensions)
            ? d : throw DocumentAccess.NotFound();
}
