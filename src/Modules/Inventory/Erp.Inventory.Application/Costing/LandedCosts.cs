using System.Globalization;
using Erp.Core.Contracts;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain.Costing;
using FluentValidation;

namespace Erp.Inventory.Application.Costing;

/// <summary>O linie a documentului CA: intrarea tinta si suma repartizata (macheta s3-01).</summary>
public sealed record LandedCostLineDto(int LineNo, long MovementId, int WarehouseId, string WarehouseCode, int ItemVariantId, string VariantCode, string VariantName,
    DateOnly MovementDate, string SourceDocumentType, string? SourceDocumentNumber, string QtyBase, string CostAmount, string? ManualAmount, string AllocatedAmount);

/// <summary>Documentul CA (S3 3.3). Status: 1 Ciorna, 2 Validat, 4 Anulat. Dupa validare: partea in stoc si partea pe iesirile deja facute.</summary>
public sealed record LandedCostDto(int Id, string? Number, DateOnly DocumentDate, DateOnly AccountingDate, byte SourceKind, long? SupplierInvoiceId, int? PartnerId,
    string? PartnerName, string Amount, byte AllocationBasis, byte Status, string? Notes, string? StockAmount, string? ExpenseAmount,
    IReadOnlyList<LandedCostLineDto> Lines, byte[] RowVersion);

public sealed record LandedCostItemDto(int Id, string? Number, DateOnly DocumentDate, DateOnly AccountingDate, byte SourceKind, long? SupplierInvoiceId,
    string? PartnerName, string Amount, byte AllocationBasis, byte Status, string? StockAmount, string? ExpenseAmount, int LineCount);

/// <summary>O intrare tinta (inv.usp_LandedCost_Targets): Id-ul cerut (miscarea sau linia sursa), miscarea, cheia si baza de repartizare.</summary>
public sealed record LandedCostTarget(long RequestedId, long MovementId, CostKey Key, decimal QtyBase, decimal CostAmount, decimal? WeightKg, decimal? VolumeM3);

public sealed record LandedCostRecord(int? Id, int CompanyId, DateOnly DocumentDate, DateOnly AccountingDate, byte SourceKind, long? SupplierInvoiceId,
    int? PartnerId, decimal Amount, byte AllocationBasis, string? Notes, IReadOnlyList<(long MovementId, decimal? ManualAmount, decimal AllocatedAmount)> Lines,
    byte[]? RowVersion);

/// <summary>Accesul la date pentru documentele CA (inv.usp_LandedCost_*).</summary>
public interface ILandedCostStore
{
    Task<IReadOnlyList<LandedCostTarget>> TargetsAsync(int companyId, IReadOnlyCollection<long> ids, string? sourceModule, CancellationToken ct);

    Task<InventorySaved> SaveAsync(LandedCostRecord record, int userId, CancellationToken ct);

    Task<LandedCostDto?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<IReadOnlyList<LandedCostItemDto>> ListAsync(int companyId, byte? status, CancellationToken ct);

    Task BeginValidationAsync(int companyId, int id, byte[]? rowVersion, CancellationToken ct);

    Task<InventorySaved> SetStatusAsync(int companyId, int id, byte status, string? number, decimal? stockAmount, decimal? expenseAmount, byte[]? rowVersion,
        int userId, CancellationToken ct);
}

/// <summary>Ciorna de salvat: tintele (miscari sau linii sursa ale modulului dat) si, la repartizarea manuala, sumele.</summary>
public sealed record LandedCostDraft(int? Id, DateOnly DocumentDate, DateOnly AccountingDate, byte SourceKind, long? SupplierInvoiceId, int? PartnerId,
    decimal Amount, byte AllocationBasis, string? Notes, string? SourceModule, IReadOnlyList<(long TargetId, decimal? ManualAmount)> Targets, byte[]? RowVersion);

/// <summary>
/// Costurile adaugate si diferentele de pret (02.05, S3 3.3, 4.2): repartizarea pe intrarile tinta (LandedCostAllocator) la salvare; la validare,
/// suma se adauga la valoarea intrarilor, iar cheile se refac cu motorul de cost (CostRecalculator, atribuit documentului): partea inca in stoc
/// ramane pe stoc (strat sau medie), partea deja iesita trece pe iesirile respective (decizia 3), inclusiv prin lantul transferurilor (4.5).
/// Se publica un singur CostStocAjustat (Kind 3, la diferenta de pret 4), cu liniile Stock pe cheie si Expense pe iesire.
/// </summary>
public sealed class LandedCostService(ILandedCostStore store, CostRecalculator recalculator, INumberSeriesService numbers, IOutbox outbox, IPeriodLock periods,
    ICurrentContext context)
{
    public const string DocumentType = "CA";

    public const string NumberSeriesType = "Inventory.LandedCost";

    public const byte LandedCostKind = 3;

    public const byte PriceDifferenceKind = 4;

    public async Task<InventorySaved> SaveAsync(int companyId, LandedCostDraft draft, CancellationToken ct)
    {
        var targets = await store.TargetsAsync(companyId, draft.Targets.Select(t => t.TargetId).Distinct().ToList(), draft.SourceModule, ct).ConfigureAwait(false);
        var byRequest = targets.ToDictionary(t => t.RequestedId);
        var ordered = draft.Targets.Select(t => (Target: byRequest[t.TargetId], t.ManualAmount)).ToList();
        IReadOnlyList<decimal> allocated;
        try
        {
            allocated = LandedCostAllocator.Allocate(draft.Amount, (AllocationBasisKind)draft.AllocationBasis, ordered.Select(o => new AllocationTarget(
                o.Target.CostAmount, o.Target.QtyBase, o.Target.WeightKg, o.Target.VolumeM3, o.ManualAmount)).ToList());
        }
        catch (AllocationException e)
        {
            throw new ErpException(ErpErrorKind.Validation, e.Code, e.Message);
        }

        var lines = ordered.Select((o, i) => (o.Target.MovementId, o.ManualAmount, allocated[i])).ToList();
        if (lines.Select(l => l.MovementId).Distinct().Count() != lines.Count)
        {
            throw new ErpException(ErpErrorKind.Validation, LandedCostAllocator.ManualMismatch, "O intrare apare de două ori în document.");
        }

        return await store.SaveAsync(new LandedCostRecord(draft.Id, companyId, draft.DocumentDate, draft.AccountingDate, draft.SourceKind, draft.SupplierInvoiceId,
            draft.PartnerId, draft.Amount, draft.AllocationBasis, draft.Notes, lines, draft.RowVersion), context.UserId, ct).ConfigureAwait(false);
    }

    public async Task<InventorySaved> ValidateAsync(int companyId, int id, byte[]? rowVersion, CancellationToken ct)
    {
        await store.BeginValidationAsync(companyId, id, rowVersion, ct).ConfigureAwait(false);
        var document = await store.GetAsync(companyId, id, ct).ConfigureAwait(false) ?? throw NotFound();
        if (await periods.IsClosedAsync(companyId, document.AccountingDate, ct).ConfigureAwait(false))
        {
            throw ErpException.Conflict(CoreErrorCodes.PeriodClosed, $"Luna datei {document.AccountingDate:dd.MM.yyyy} este închisă.");
        }

        // Tintele se reverifica (intre timp o receptie poate fi stornata), apoi cheile se aduc la zi: ce era in coada tine de recalculare, nu de CA.
        var targets = await store.TargetsAsync(companyId, document.Lines.Select(l => l.MovementId).ToList(), null, ct).ConfigureAwait(false);
        var keys = targets.Select(t => t.Key).Distinct().ToList();
        await recalculator.RunAsync(companyId, keys, null, ct).ConfigureAwait(false);

        var deltas = document.Lines.ToDictionary(l => l.MovementId, l => Parse(l.AllocatedAmount));
        if (targets.Any(t => t.CostAmount + deltas[t.MovementId] < 0))
        {
            throw new ErpException(ErpErrorKind.Validation, "INV_COST_NEGATIVE",
                "Reducerea depășește valoarea unei intrări; costul unitar ar deveni negativ. Repartizați reducerea pe mai multe intrări.");
        }

        var kind = document.SourceKind == 2 ? PriceDifferenceKind : LandedCostKind;
        var changes = await recalculator.RunAsync(companyId, keys, new CostAttribution(kind, DocumentType, id, document.AccountingDate, deltas), ct)
            .ConfigureAwait(false);

        var lines = Split(targets, deltas, changes);
        var stock = lines.Where(l => l.TargetKind == "Stock").Sum(l => l.Amount);
        var expense = lines.Where(l => l.TargetKind == "Expense").Sum(l => l.Amount);
        var number = await numbers.NextAsync(companyId, NumberSeriesType, document.DocumentDate, ct).ConfigureAwait(false);
        await outbox.EnqueueAsync(new CostStocAjustat(kind, "inv", DocumentType, id, document.AccountingDate, lines)
        {
            CompanyId = companyId, Accounting = StockAccounting.Adjustment(kind, DocumentType, id, document.AccountingDate, lines),
        }, ct).ConfigureAwait(false);
        return await store.SetStatusAsync(companyId, id, 2, number.Formatted, stock, expense, null, context.UserId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Impartirea efectului intre stoc si cheltuiala, din schimbarile de cost: iesirile (fara perechile transferurilor) sunt Expense, fiecare cu
    /// miscarea ei; restul ramane pe stoc, pe cheie. Ce nu se regaseste in schimbari (regularizari, lant oprit la limita) ramane pe prima tinta.
    /// </summary>
    internal static IReadOnlyList<CostAdjustmentEventLine> Split(IReadOnlyList<LandedCostTarget> targets, IReadOnlyDictionary<long, decimal> deltas,
        IReadOnlyList<CostChange> changes)
    {
        var transferPairs = changes.Where(c => c.Movement.CounterMovementId is not null).Select(c => c.Movement.CounterMovementId!.Value).ToHashSet();
        var targetIds = targets.Select(t => t.MovementId).ToHashSet();
        var expense = changes.Where(c => !targetIds.Contains(c.Movement.Id) && !transferPairs.Contains(c.Movement.Id) && c.Movement.CounterMovementId is null)
            .Select(c => new CostAdjustmentEventLine(c.Key.WarehouseId, c.Key.ItemVariantId, c.Movement.Id, -c.Delta, "Expense"))
            .Where(l => l.Amount != 0)
            .ToList();
        var expenseIds = expense.Select(e => e.SourceMovementId).ToHashSet();
        var stock = changes.Where(c => !expenseIds.Contains(c.Movement.Id))
            .GroupBy(c => c.Key)
            .Select(g => (Key: g.Key, Amount: g.Sum(c => c.Delta),
                Movement: targets.FirstOrDefault(t => t.Key == g.Key)?.MovementId ?? g.First().Movement.Id))
            .ToList();
        var stockByKey = stock.ToDictionary(s => s.Key, s => (s.Amount, s.Movement));
        var first = targets[0];
        var rest = deltas.Values.Sum() - expense.Sum(e => e.Amount) - stock.Sum(s => s.Amount);
        if (rest != 0)
        {
            stockByKey[first.Key] = stockByKey.TryGetValue(first.Key, out var current) ? (current.Amount + rest, current.Movement) : (rest, first.MovementId);
        }

        return stockByKey.Where(s => s.Value.Amount != 0)
            .Select(s => new CostAdjustmentEventLine(s.Key.WarehouseId, s.Key.ItemVariantId, s.Value.Movement, s.Value.Amount, "Stock"))
            .Concat(expense)
            .ToList();
    }

    public static ErpException NotFound() => ErpException.NotFound("INV_LANDED_COST_NOT_FOUND", "Documentul de costuri adăugate nu există în firma curentă.");

    public static decimal Parse(string? value) => decimal.Parse(value ?? "0", NumberStyles.Number, CultureInfo.InvariantCulture);

    public static bool TryParse(string? value, out decimal amount) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
        && decimal.Round(amount, 2) == amount;
}

public sealed record LandedCostLineInput(long MovementId, string? ManualAmount);

/// <summary>Documentele CA ale firmei curente (macheta s3-01).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostView)]
public sealed record GetLandedCosts(byte? Status = null) : IQuery<IReadOnlyList<LandedCostItemDto>>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.CostView)]
public sealed record GetLandedCost(int Id) : IQuery<LandedCostDto>;

/// <summary>Salveaza ciorna documentului CA, cu repartizarea propusa (S3 3.3). Suma e fara TVA, in RON, negativa la reducere.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LandedCostEdit)]
[Audited("Inv.LandedCost.Save")]
public sealed record SaveLandedCost(int? Id, DateOnly DocumentDate, DateOnly? AccountingDate, byte SourceKind, long? SupplierInvoiceId, int? PartnerId,
    string Amount, byte AllocationBasis, string? Notes, IReadOnlyList<LandedCostLineInput> Lines, byte[]? RowVersion) : ICommand<InventorySaved>;

/// <summary>Valideaza documentul CA: ajusteaza costul intrarilor, reface cheile, publica CostStocAjustat (S3 4.2).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LandedCostValidate)]
[Audited("Inv.LandedCost.Validate")]
public sealed record ValidateLandedCost(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LandedCostEdit)]
[Audited("Inv.LandedCost.Cancel")]
public sealed record CancelLandedCost(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

internal sealed class SaveLandedCostValidator : AbstractValidator<SaveLandedCost>
{
    public SaveLandedCostValidator()
    {
        RuleFor(x => x.Amount).Must(a => LandedCostService.TryParse(a, out var v) && v != 0)
            .WithMessage("Suma este o valoare nenulă, cu cel mult două zecimale.");
        RuleFor(x => x.SourceKind).InclusiveBetween((byte)1, (byte)3).WithMessage("Tipul costului adăugat nu e cunoscut.");
        RuleFor(x => x.AllocationBasis).InclusiveBetween((byte)1, (byte)5).WithMessage("Baza de repartizare nu e cunoscută.");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Documentul are cel puțin o intrare țintă.");
        RuleForEach(x => x.Lines).Must(l => string.IsNullOrWhiteSpace(l.ManualAmount) || LandedCostService.TryParse(l.ManualAmount, out _))
            .WithMessage("Suma manuală are cel mult două zecimale.");
        RuleFor(x => x.Notes).MaximumLength(1000).WithMessage("Observațiile au cel mult 1000 de caractere.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class LandedCostHandler(ILandedCostStore store, LandedCostService service, ICurrentContext context, IAuditScope audit)
    : IQueryHandler<GetLandedCosts, IReadOnlyList<LandedCostItemDto>>, IQueryHandler<GetLandedCost, LandedCostDto>, ICommandHandler<SaveLandedCost, InventorySaved>,
    ICommandHandler<ValidateLandedCost, InventorySaved>, ICommandHandler<CancelLandedCost, InventorySaved>
{
    public Task<IReadOnlyList<LandedCostItemDto>> Handle(GetLandedCosts request, CancellationToken cancellationToken) =>
        store.ListAsync(context.CompanyId, request.Status, cancellationToken);

    public async Task<LandedCostDto> Handle(GetLandedCost request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) ?? throw LandedCostService.NotFound();

    public async Task<InventorySaved> Handle(SaveLandedCost request, CancellationToken cancellationToken)
    {
        var draft = new LandedCostDraft(request.Id, request.DocumentDate, request.AccountingDate ?? request.DocumentDate, request.SourceKind,
            request.SupplierInvoiceId, request.PartnerId, LandedCostService.Parse(request.Amount), request.AllocationBasis,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(), null,
            request.Lines.Select(l => (l.MovementId, string.IsNullOrWhiteSpace(l.ManualAmount) ? (decimal?)null : LandedCostService.Parse(l.ManualAmount))).ToList(),
            request.RowVersion);
        var saved = await service.SaveAsync(context.CompanyId, draft, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.LandedCost", saved.Id, new { draft.SourceKind, draft.Amount, draft.AllocationBasis, Lines = draft.Targets.Count });
        return saved;
    }

    public async Task<InventorySaved> Handle(ValidateLandedCost request, CancellationToken cancellationToken)
    {
        var saved = await service.ValidateAsync(context.CompanyId, request.Id, request.RowVersion, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.LandedCost", request.Id, new { Validated = true });
        return saved;
    }

    public async Task<InventorySaved> Handle(CancelLandedCost request, CancellationToken cancellationToken)
    {
        var saved = await store.SetStatusAsync(context.CompanyId, request.Id, 4, null, null, null, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Inv.LandedCost", request.Id, new { Cancelled = true });
        return saved;
    }
}
