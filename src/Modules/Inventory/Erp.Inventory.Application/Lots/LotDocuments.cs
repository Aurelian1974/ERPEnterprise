using Erp.Core.Contracts;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using FluentValidation;

namespace Erp.Inventory.Application.Lots;

public sealed record LotChangeLineInput(int ItemVariantId, int FromLotId, int? ToLotId, string? NewLotCode, DateOnly? NewExpiryDate, string QtyBase);

public sealed record LotChangeLineDto(long LineId, int LineNumber, int ItemVariantId, string VariantCode, int FromLotId, string FromCku, int? ToLotId, string? ToCku,
    string? NewLotCode, DateOnly? NewExpiryDate, string QtyBase);

/// <summary>Documentul SL (S4 3.7). Status: 1 Ciorna, 2 Validat, 4 Anulat.</summary>
public sealed record LotChangeDto(int Id, string? Number, int WarehouseId, string WarehouseCode, int? DepotId, DateOnly DocumentDate, byte Status, string? Notes,
    IReadOnlyList<LotChangeLineDto> Lines, byte[] RowVersion);

public sealed record LotChangeItemDto(int Id, string? Number, int WarehouseId, string WarehouseCode, DateOnly DocumentDate, byte Status, int LineCount);

public sealed record RecallLotDto(int LotId, string CkuCode, string LotCode, string? SupplierLotCode, LotStatus Status, string QtyOnHand);

public sealed record RecallCustomerDto(long Id, int LotId, string CkuCode, int? PartnerId, string? PartnerName, string SourceDocumentType, long SourceDocumentId,
    string? SourceDocumentNumber, DateOnly MovementDate, string QtyBase, byte? Outcome, string? OutcomeNote);

/// <summary>Retragerea RL (S4 3.6; macheta s4-05). Status: 1 Ciorna, 2 Deschisa, 3 Inchisa, 4 Anulata.</summary>
public sealed record RecallDto(int Id, string? Number, DateOnly DocumentDate, byte SourceKind, string Reason, int? SupplierPartnerId, string? SupplierName,
    string? SupplierLotCode, byte Status, DateTime? ValidatedAt, DateTime? ClosedAt, IReadOnlyList<RecallLotDto> Lots, IReadOnlyList<RecallCustomerDto> Customers,
    byte[] RowVersion);

public sealed record RecallItemDto(int Id, string? Number, DateOnly DocumentDate, byte SourceKind, string? SupplierLotCode, byte Status, int LotCount,
    int CustomerCount);

public sealed record LotTraceMovementDto(int Depth, long MovementId, int LotId, string CkuCode, DateOnly MovementDate, string MovementTypeCode,
    string WarehouseCode, string QtyBase, int? PartnerId, string? PartnerName, string SourceDocumentType, long SourceDocumentId, string? SourceDocumentNumber);

public sealed record LotTraceOriginDto(int Depth, int LotId, string CkuCode, string? SupplierLotCode, string? SupplierName, DateOnly? FirstReceiptDate);

/// <summary>Trasabilitatea unui lot (S4 3.6; macheta s4-05): lotul, inapoi, inainte, stocul acum.</summary>
public sealed record LotTraceDto(int LotId, string CkuCode, string? SupplierLotCode, string? SupplierName, DateOnly? FirstReceiptDate, string? ReceiptDocument,
    IReadOnlyList<LotTraceOriginDto> Back, IReadOnlyList<LotTraceMovementDto> Forward, IReadOnlyList<LotStockDto> StockNow);

/// <summary>Accesul la date pentru SL, RL si trasabilitate (inv.usp_LotChange_*, inv.usp_Recall_*, inv.usp_Lot_Trace).</summary>
public interface ILotDocumentStore
{
    Task<InventorySaved> SaveLotChangeAsync(int companyId, int? id, int warehouseId, int? depotId, DateOnly date, string? notes,
        IReadOnlyList<(int ItemVariantId, int FromLotId, int? ToLotId, string? NewLotCode, DateOnly? NewExpiryDate, decimal Qty)> lines, byte[]? rowVersion,
        int userId, CancellationToken ct);

    Task<LotChangeDto?> GetLotChangeAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<LotChangeItemDto>> ListLotChangesAsync(int companyId, CancellationToken ct);

    Task<InventorySaved> SetLotChangeStatusAsync(int companyId, int id, byte status, string? number, byte[] rowVersion, bool check, int userId, CancellationToken ct);

    Task<InventorySaved> SaveRecallAsync(int companyId, int? id, DateOnly date, byte sourceKind, string reason, int? supplierPartnerId, string? supplierLotCode,
        IReadOnlyList<int> lotIds, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<RecallDto?> GetRecallAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<RecallItemDto>> ListRecallsAsync(int companyId, CancellationToken ct);

    Task<(InventorySaved Saved, IReadOnlyList<(int LotId, LotStatus Status, int? SupplierPartnerId)> Lots, int Customers)> ValidateRecallAsync(int companyId, int id,
        byte[] rowVersion, string number, int userId, CancellationToken ct);

    Task<InventorySaved> CloseRecallAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct);

    Task SetRecallOutcomeAsync(int companyId, long customerLineId, byte outcome, string? note, int userId, CancellationToken ct);

    Task<LotTraceDto?> TraceAsync(int companyId, int lotId, int maxDepth, CancellationToken ct);
}

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record GetLotChanges : IQuery<IReadOnlyList<LotChangeItemDto>>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record GetLotChange(int Id) : IQuery<LotChangeDto>;

/// <summary>Ciorna schimbarii de lot (S4 3.7): mutarea cantitatii de pe un lot pe altul, in aceeasi gestiune.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotChangeEdit)]
[Audited("Inv.LotChange.Save")]
public sealed record SaveLotChange(int? Id, int WarehouseId, int? DepotId, DateOnly DocumentDate, string? Notes, IReadOnlyList<LotChangeLineInput> Lines,
    byte[]? RowVersion) : ICommand<InventorySaved>;

/// <summary>Valideaza SL: doua miscari pe linie (LOT_CHANGE_OUT / LOT_CHANGE_IN), la cost, fara nota contabila.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotChangeValidate)]
[Audited("Inv.LotChange.Validate")]
public sealed record ValidateLotChange(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotChangeEdit)]
[Audited("Inv.LotChange.Cancel")]
public sealed record CancelLotChange(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record GetRecalls : IQuery<IReadOnlyList<RecallItemDto>>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record GetRecall(int Id) : IQuery<RecallDto>;

/// <summary>Ciorna retragerii (S4 3.6): loturile sau lotul furnizorului, motivul si sursa.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.RecallEdit)]
[Audited("Inv.Recall.Save")]
public sealed record SaveRecall(int? Id, DateOnly DocumentDate, byte SourceKind, string Reason, int? SupplierPartnerId, string? SupplierLotCode,
    IReadOnlyList<int> LotIds, byte[]? RowVersion) : ICommand<InventorySaved>;

/// <summary>Valideaza retragerea: loturile trec in Retras in toata firma, instantaneul clientilor, RetragereValidata.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.RecallValidate)]
[Audited("Inv.Recall.Validate")]
public sealed record ValidateRecall(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.RecallClose)]
[Audited("Inv.Recall.Close")]
public sealed record CloseRecall(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Rezultatul la un client al retragerii: 1 returnat, 2 distrus la client, 3 nu mai are.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.RecallEdit)]
[Audited("Inv.Recall.Outcome")]
public sealed record SetRecallOutcome(long CustomerLineId, byte Outcome, string? Note) : ICommand<bool>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record TraceLot(int LotId, int MaxDepth = 10) : IQuery<LotTraceDto>;

/// <summary>Citirea unui cod GS1 (S4 3.8), pentru ecranele de receptie si scanare.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record ParseGs1(string Raw) : IQuery<Gs1ParseResult>;

internal sealed class SaveLotChangeValidator : AbstractValidator<SaveLotChange>
{
    public SaveLotChangeValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Documentul are cel puțin o linie.");
        RuleForEach(x => x.Lines).Must(l => Quantity.TryParse(l.QtyBase, out var q) && q > 0).WithMessage("Cantitatea este pozitivă.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveRecallValidator : AbstractValidator<SaveRecall>
{
    public SaveRecallValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Motivul este obligatoriu.").MaximumLength(1000);
        RuleFor(x => x.SourceKind).InclusiveBetween((byte)1, (byte)3);
        RuleFor(x => x).Must(x => x.LotIds.Count > 0 || !string.IsNullOrWhiteSpace(x.SupplierLotCode))
            .WithMessage("Retragerea are cel puțin un lot sau lotul furnizorului.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

/// <summary>
/// Documentele de lot (02.05, S4 3.6, 3.7): schimbarea lotului (SL) prin portul de stoc, cu perechea LOT_CHANGE_OUT / LOT_CHANGE_IN la cost si
/// fara nota; retragerea (RL), care trece loturile in Retras in toata firma (LotService, cu istoric si eveniment) si pastreaza clientii.
/// </summary>
internal sealed class LotDocumentHandler(ILotDocumentStore store, StockLedger ledger, LotService lots, INumberSeriesService numbers, IOutbox outbox,
    IGs1BarcodeParser gs1, ICurrentContext context, IAuditScope audit)
    : IQueryHandler<GetLotChanges, IReadOnlyList<LotChangeItemDto>>, IQueryHandler<GetLotChange, LotChangeDto>, ICommandHandler<SaveLotChange, InventorySaved>,
    ICommandHandler<ValidateLotChange, InventorySaved>, ICommandHandler<CancelLotChange, InventorySaved>, IQueryHandler<GetRecalls, IReadOnlyList<RecallItemDto>>,
    IQueryHandler<GetRecall, RecallDto>, ICommandHandler<SaveRecall, InventorySaved>, ICommandHandler<ValidateRecall, InventorySaved>,
    ICommandHandler<CloseRecall, InventorySaved>, ICommandHandler<SetRecallOutcome, bool>, IQueryHandler<TraceLot, LotTraceDto>, IQueryHandler<ParseGs1, Gs1ParseResult>
{
    private static ErpException NotFound() => ErpException.NotFound("INV_DOCUMENT_NOT_FOUND", "Documentul nu există în firma curentă.");

    public Task<IReadOnlyList<LotChangeItemDto>> Handle(GetLotChanges request, CancellationToken cancellationToken) =>
        store.ListLotChangesAsync(context.CompanyId, cancellationToken);

    public async Task<LotChangeDto> Handle(GetLotChange request, CancellationToken cancellationToken) =>
        await store.GetLotChangeAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();

    public async Task<InventorySaved> Handle(SaveLotChange request, CancellationToken cancellationToken)
    {
        var saved = await store.SaveLotChangeAsync(context.CompanyId, request.Id, request.WarehouseId, request.DepotId, request.DocumentDate,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            request.Lines.Select(l => (l.ItemVariantId, l.FromLotId, l.ToLotId, l.NewLotCode, l.NewExpiryDate, Quantity.ParseOptional(l.QtyBase) ?? 0)).ToList(),
            request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.LotChange", saved.Id, new { request.WarehouseId, Lines = request.Lines.Count });
        return saved;
    }

    public async Task<InventorySaved> Handle(ValidateLotChange request, CancellationToken cancellationToken)
    {
        var document = await store.GetLotChangeAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        await store.SetLotChangeStatusAsync(context.CompanyId, request.Id, 2, null, request.RowVersion, true, context.UserId, cancellationToken).ConfigureAwait(false);
        var number = await numbers.NextAsync(context.CompanyId, "Inventory.LotChange", document.DocumentDate, cancellationToken).ConfigureAwait(false);
        var lines = document.Lines.SelectMany(l => new[]
        {
            new StockMovementRequest(l.LineId, 1, "LOT_CHANGE_OUT", document.DepotId, document.WarehouseId, l.ItemVariantId, -(Quantity.ParseOptional(l.QtyBase) ?? 0),
                null, l.FromLotId, null, null, null, null, null, null),
            new StockMovementRequest(l.LineId, 2, "LOT_CHANGE_IN", document.DepotId, document.WarehouseId, l.ItemVariantId, Quantity.ParseOptional(l.QtyBase) ?? 0,
                null, l.ToLotId, l.NewLotCode, l.NewExpiryDate, null, null, null, null) { ConfirmNewExpiry = true },
        }).ToList();
        await ledger.PostWithOutcomeAsync(new StockPostingRequest(context.CompanyId, "inv", "SL", document.Id, number.Formatted, document.DocumentDate,
            document.DocumentDate, null, false, lines), null, cancellationToken).ConfigureAwait(false);
        var saved = await store.SetLotChangeStatusAsync(context.CompanyId, request.Id, 2, number.Formatted, request.RowVersion, false, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.LotChange", request.Id, new { Number = number.Formatted });
        return saved;
    }

    public async Task<InventorySaved> Handle(CancelLotChange request, CancellationToken cancellationToken)
    {
        var saved = await store.SetLotChangeStatusAsync(context.CompanyId, request.Id, 4, null, request.RowVersion, false, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Inv.LotChange", request.Id, new { Cancelled = true });
        return saved;
    }

    public Task<IReadOnlyList<RecallItemDto>> Handle(GetRecalls request, CancellationToken cancellationToken) => store.ListRecallsAsync(context.CompanyId, cancellationToken);

    public async Task<RecallDto> Handle(GetRecall request, CancellationToken cancellationToken) =>
        await store.GetRecallAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();

    public async Task<InventorySaved> Handle(SaveRecall request, CancellationToken cancellationToken)
    {
        var saved = await store.SaveRecallAsync(context.CompanyId, request.Id, request.DocumentDate, request.SourceKind, request.Reason.Trim(),
            request.SupplierPartnerId, string.IsNullOrWhiteSpace(request.SupplierLotCode) ? null : request.SupplierLotCode.Trim(), request.LotIds, request.RowVersion,
            context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Recall", saved.Id, new { request.SupplierLotCode, Lots = request.LotIds.Count });
        return saved;
    }

    public async Task<InventorySaved> Handle(ValidateRecall request, CancellationToken cancellationToken)
    {
        var document = await store.GetRecallAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        var number = await numbers.NextAsync(context.CompanyId, "Inventory.Recall", document.DocumentDate, cancellationToken).ConfigureAwait(false);
        var (saved, recalled, customers) = await store.ValidateRecallAsync(context.CompanyId, request.Id, request.RowVersion, number.Formatted, context.UserId,
            cancellationToken).ConfigureAwait(false);
        foreach (var lot in recalled.Where(l => l.Status != LotStatus.Recalled))
        {
            await lots.ChangeStatusAsync(context.CompanyId, lot.LotId, LotStatus.Recalled, document.Reason, null, "RL", request.Id, true, cancellationToken)
                .ConfigureAwait(false);
        }

        await outbox.EnqueueAsync(new RetragereValidata(request.Id, number.Formatted, recalled.Select(l => l.LotId).ToList(), document.SupplierLotCode,
            document.SupplierPartnerId ?? recalled.Select(l => l.SupplierPartnerId).FirstOrDefault(p => p is not null), document.Reason, customers)
        {
            CompanyId = context.CompanyId,
        }, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Recall", request.Id, new { Number = number.Formatted, Lots = recalled.Count, Customers = customers });
        return saved;
    }

    public async Task<InventorySaved> Handle(CloseRecall request, CancellationToken cancellationToken)
    {
        var saved = await store.CloseRecallAsync(context.CompanyId, request.Id, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Recall", request.Id, new { Closed = true });
        return saved;
    }

    public async Task<bool> Handle(SetRecallOutcome request, CancellationToken cancellationToken)
    {
        await store.SetRecallOutcomeAsync(context.CompanyId, request.CustomerLineId, request.Outcome, request.Note?.Trim(), context.UserId, cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task<LotTraceDto> Handle(TraceLot request, CancellationToken cancellationToken) =>
        await store.TraceAsync(context.CompanyId, request.LotId, Math.Clamp(request.MaxDepth, 1, 10), cancellationToken).ConfigureAwait(false)
        ?? throw ErpException.NotFound("INV_LOT_NOT_FOUND", "Lotul nu există în firma curentă.");

    public Task<Gs1ParseResult> Handle(ParseGs1 request, CancellationToken cancellationToken) => Task.FromResult(gs1.Parse(request.Raw));
}
