using Erp.Core.Contracts;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using FluentValidation;

namespace Erp.Inventory.Application.Costing;

public sealed record ValuationChangeItemDto(int ItemId, string ItemCode, string ItemName, ValuationMethod OldMethod);

/// <summary>Previzualizarea pe o cheie (macheta s1-04): stocul, straturile deschise, valoarea si costul mediu (valoarea ramane aceeasi).</summary>
public sealed record ValuationChangePreviewDto(string VariantCode, string VariantName, string WarehouseCode, string? DepotCode, string Qty, int OpenLayers,
    string Value, string? AverageCost);

/// <summary>Documentul SM (S1 4.5; macheta s1-04). Status: 1 Ciorna, 2 Validat, 4 Anulat.</summary>
public sealed record ValuationChangeDto(int Id, string? Number, ValuationMethod NewMethod, DateOnly EffectiveDate, string Reason, byte Status, DateTime CreatedAt,
    string? CreatedByName, IReadOnlyList<ValuationChangeItemDto> Items, IReadOnlyList<ValuationChangePreviewDto> Preview, byte[] RowVersion);

public sealed record ValuationChangeListItemDto(int Id, string? Number, ValuationMethod NewMethod, DateOnly EffectiveDate, byte Status, int ItemCount,
    string? FirstItemName);

/// <summary>Un strat de cost (macheta s3-05): gestiunea, depozitul, intrarea care l-a creat (si originea, la transfer), data, cantitatile si costul.</summary>
public sealed record CostLayerDto(long Id, int WarehouseId, string WarehouseCode, string? DepotCode, int? LotId, string SourceDocument, string? OriginDocument,
    DateOnly LayerDate, string QtyIn, string QtyRemaining, string UnitCost, string ValueRemaining, bool IsNegative);

/// <summary>Un consum de strat (macheta s3-05): iesirea, cantitatea si valoarea luate din strat.</summary>
public sealed record CostConsumptionDto(DateOnly MovementDate, string Outbound, string Qty, string LayerDocument, string UnitCost, string Value);

public sealed record CostLayersDto(IReadOnlyList<CostLayerDto> Layers, IReadOnlyList<CostConsumptionDto> Consumptions);

/// <summary>O ajustare de cost (macheta s3-02): tipul, miscarea si documentul ei, suma, daca schimba stocul.</summary>
public sealed record CostAdjustmentDto(long Id, byte Kind, DateOnly AccountingDate, string WarehouseCode, string VariantCode, long? MovementId,
    string? SourceDocumentType, long? SourceDocumentId, string Amount, bool InStock, DateTime CreatedAt);

/// <summary>Accesul la date pentru schimbarea metodei, straturi si ajustari (inv.usp_ValuationMethodChange_*, inv.usp_CostLayer_List, inv.usp_CostAdjustment_List).</summary>
public interface IValuationViewStore
{
    Task<InventorySaved> SaveChangeAsync(int companyId, int? id, ValuationMethod method, DateOnly effectiveDate, string reason, IReadOnlyList<int> itemIds,
        byte[]? rowVersion, int userId, CancellationToken ct);

    Task<ValuationChangeDto?> GetChangeAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<ValuationChangeListItemDto>> ListChangesAsync(int companyId, byte? status, CancellationToken ct);

    Task<(InventorySaved Saved, IReadOnlyList<(int ItemId, ValuationMethod OldMethod)> Items, IReadOnlyList<CostKey> Keys)> ValidateChangeAsync(int companyId,
        int id, byte[] rowVersion, string number, DateOnly today, int userId, CancellationToken ct);

    Task<InventorySaved> CancelChangeAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct);

    Task<CostLayersDto> LayersAsync(int companyId, int itemVariantId, int? warehouseId, bool openOnly, CancellationToken ct);

    Task<IReadOnlyList<CostAdjustmentDto>> AdjustmentsAsync(int companyId, byte? kind, int top, CancellationToken ct);
}

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostView)]
public sealed record GetValuationChanges(byte? Status = null) : IQuery<IReadOnlyList<ValuationChangeListItemDto>>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostView)]
public sealed record GetValuationChange(int Id) : IQuery<ValuationChangeDto>;

/// <summary>Salveaza ciorna documentului de schimbare a metodei (S1 4.5): modelele, metoda noua, prima zi a lunii, motivul.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.ValuationMethodChange)]
[Audited("Inv.ValuationMethodChange.Save")]
public sealed record SaveValuationChange(int? Id, ValuationMethod NewMethod, DateOnly EffectiveDate, string Reason, IReadOnlyList<int> ItemIds, byte[]? RowVersion)
    : ICommand<InventorySaved>;

/// <summary>Valideaza schimbarea: metoda noua pe modele, cheile refacute cu conversia straturilor la data (valoarea neschimbata), MetodaEvaluareSchimbata.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.ValuationMethodChange)]
[Audited("Inv.ValuationMethodChange.Validate")]
public sealed record ValidateValuationChange(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.ValuationMethodChange)]
[Audited("Inv.ValuationMethodChange.Cancel")]
public sealed record CancelValuationChange(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Straturile de cost ale unei variante, eventual intr-o gestiune, si ultimele lor consumuri (macheta s3-05).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostView)]
public sealed record GetCostLayers(int ItemVariantId, int? WarehouseId = null, bool OpenOnly = true) : IQuery<CostLayersDto>;

/// <summary>Ultimele ajustari de cost (macheta s3-02): recalculari, regularizari, costuri adaugate.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostView)]
public sealed record GetCostAdjustments(byte? Kind = null) : IQuery<IReadOnlyList<CostAdjustmentDto>>;

internal sealed class SaveValuationChangeValidator : AbstractValidator<SaveValuationChange>
{
    public SaveValuationChangeValidator()
    {
        RuleFor(x => x.ItemIds).NotEmpty().WithMessage("Documentul are cel puțin un articol.");
        RuleFor(x => x.EffectiveDate).Must(d => d.Day == 1).WithMessage("Schimbarea metodei se aplică de la prima zi a unei luni.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Motivul este obligatoriu (apare în notele explicative).").MaximumLength(1000);
        RuleFor(x => x.NewMethod).IsInEnum().WithMessage("Metoda nu e cunoscută.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

/// <summary>
/// Schimbarea metodei de evaluare (02.05, S1 4.5) si vederile de cost (S3 11): la validare cheile modelelor se refac cu motorul de cost, care
/// aplica vechea metoda inainte de data si converteste straturile la data (CostEngine.ChangeMethod); valoarea stocului nu se schimba, deci nu
/// apare nicio nota contabila.
/// </summary>
internal sealed class ValuationViewHandler(IValuationViewStore store, CostRecalculator recalculator, INumberSeriesService numbers, IOutbox outbox,
    IPeriodLock periods, ICurrentContext context, TimeProvider time, IAuditScope audit)
    : IQueryHandler<GetValuationChanges, IReadOnlyList<ValuationChangeListItemDto>>, IQueryHandler<GetValuationChange, ValuationChangeDto>,
    ICommandHandler<SaveValuationChange, InventorySaved>, ICommandHandler<ValidateValuationChange, InventorySaved>,
    ICommandHandler<CancelValuationChange, InventorySaved>, IQueryHandler<GetCostLayers, CostLayersDto>,
    IQueryHandler<GetCostAdjustments, IReadOnlyList<CostAdjustmentDto>>
{
    public const string NumberSeriesType = "Inventory.ValuationMethodChange";

    public Task<IReadOnlyList<ValuationChangeListItemDto>> Handle(GetValuationChanges request, CancellationToken cancellationToken) =>
        store.ListChangesAsync(context.CompanyId, request.Status, cancellationToken);

    public async Task<ValuationChangeDto> Handle(GetValuationChange request, CancellationToken cancellationToken) =>
        await store.GetChangeAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();

    public async Task<InventorySaved> Handle(SaveValuationChange request, CancellationToken cancellationToken)
    {
        var saved = await store.SaveChangeAsync(context.CompanyId, request.Id, request.NewMethod, request.EffectiveDate, request.Reason.Trim(),
            request.ItemIds.Distinct().ToList(), request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.ValuationMethodChange", saved.Id, new { request.NewMethod, request.EffectiveDate, Items = request.ItemIds.Count });
        return saved;
    }

    public async Task<InventorySaved> Handle(ValidateValuationChange request, CancellationToken cancellationToken)
    {
        var document = await store.GetChangeAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        if (await periods.IsClosedAsync(context.CompanyId, document.EffectiveDate, cancellationToken).ConfigureAwait(false))
        {
            throw ErpException.Conflict("INV_VALUATION_CHANGE_INVALID", $"Luna datei {document.EffectiveDate:dd.MM.yyyy} este închisă.");
        }

        var number = await numbers.NextAsync(context.CompanyId, NumberSeriesType, document.EffectiveDate, cancellationToken).ConfigureAwait(false);
        var (saved, items, keys) = await store.ValidateChangeAsync(context.CompanyId, request.Id, request.RowVersion, number.Formatted,
            BusinessDay.Today(time), context.UserId, cancellationToken).ConfigureAwait(false);
        await recalculator.RunAsync(context.CompanyId, keys, null, cancellationToken).ConfigureAwait(false);
        await outbox.EnqueueAsync(new MetodaEvaluareSchimbata(request.Id, number.Formatted, items.Select(i => i.ItemId).ToList(),
            items.Select(i => i.OldMethod).ToList(), document.NewMethod, document.EffectiveDate, document.Reason) { CompanyId = context.CompanyId },
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.ValuationMethodChange", request.Id, new { Number = number.Formatted, Keys = keys.Count });
        return saved;
    }

    public async Task<InventorySaved> Handle(CancelValuationChange request, CancellationToken cancellationToken)
    {
        var saved = await store.CancelChangeAsync(context.CompanyId, request.Id, request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.ValuationMethodChange", request.Id, new { Cancelled = true });
        return saved;
    }

    public Task<CostLayersDto> Handle(GetCostLayers request, CancellationToken cancellationToken) =>
        store.LayersAsync(context.CompanyId, request.ItemVariantId, request.WarehouseId, request.OpenOnly, cancellationToken);

    public Task<IReadOnlyList<CostAdjustmentDto>> Handle(GetCostAdjustments request, CancellationToken cancellationToken) =>
        store.AdjustmentsAsync(context.CompanyId, request.Kind, 200, cancellationToken);

    private static ErpException NotFound() =>
        ErpException.NotFound("INV_VALUATION_CHANGE_NOT_FOUND", "Documentul de schimbare a metodei nu există în firma curentă.");
}
