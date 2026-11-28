using Erp.Core.Contracts;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;

namespace Erp.Inventory.Application.Movements;

/// <summary>Rezultatul unei inregistrari: miscarile si daca apelul a fost o reluare (sursa deja inregistrata, S2 regula 6).</summary>
public sealed record StockPostingOutcome(IReadOnlyList<PostedStockMovement> Movements, bool Replayed);

public sealed record PostedStockMovement(long MovementId, long SourceLineId, byte SourceRole, int WarehouseId, int? DepotId, int ItemVariantId, decimal QtyBase,
    decimal UnitCost, decimal CostAmount, bool IsForcedNegative, string MovementTypeCode, string? ReasonCode);

/// <summary>Setarile de data ale firmei (S1 3.3), citite la fiecare inregistrare.</summary>
public sealed record BackdatingSettings(short BackdatingDays, bool BackdatingWithRight);

/// <summary>Accesul la date pentru registru (inv.usp_StockMovement_Post, _Reverse, inv.usp_Stock_Availability).</summary>
public interface IStockMovementStore
{
    Task<BackdatingSettings> GetBackdatingAsync(int companyId, CancellationToken ct);

    Task<StockPostingOutcome> PostAsync(StockPostingRequest request, int? employeeId, int userId, CancellationToken ct);

    Task<StockPostingOutcome> ReverseAsync(StockReversalRequest request, int userId, CancellationToken ct);

    Task<IReadOnlyList<StockAvailabilityDto>> GetAvailabilityAsync(int companyId, int? depotId, int? warehouseId, IReadOnlyCollection<int> itemVariantIds,
        DateOnly onDate, CancellationToken ct);
}

/// <summary>
/// Portul de stoc implementat de Stocuri (02.05, S2 4.1 si 6): data miscarii (BackdatingDays, dreptul Inv.Movement.Backdate, 54103), luna
/// deschisa (Nucleu, IPeriodLock), inregistrarea in tranzactia apelantului si evenimentele MiscareStocValidata si StocNegativFortat in
/// outbox, oricare ar fi sursa. Faptele din afara ERP (POS) nu trec prin verificarea datei. Fara modulul Stocuri licentiat nu scrie nimic.
/// </summary>
public sealed class StockLedger(IStockMovementStore store, ILicenseInfo license, IPeriodLock periods, ICurrentContext context, IOutbox outbox,
    TimeProvider time) : IStockLedger
{
    public bool IsEnabled => license.IsModuleActive(InventoryModule.Code);

    public async Task<StockPostingResult> PostAsync(StockPostingRequest request, CancellationToken ct) =>
        Result(await PostWithOutcomeAsync(request, null, ct).ConfigureAwait(false));

    /// <summary>Inregistrarea cu angajatul documentului (bon de consum) si cu rezultatul complet, pentru documentele Stocurilor.</summary>
    public async Task<StockPostingOutcome> PostWithOutcomeAsync(StockPostingRequest request, int? employeeId, CancellationToken ct)
    {
        if (!IsEnabled || request.Lines.Count == 0)
        {
            return new StockPostingOutcome([], false);
        }

        if (!request.IsExternalFact)
        {
            await EnsureDateAsync(request.CompanyId, request.MovementDate, request.AccountingDate, ct).ConfigureAwait(false);
        }

        var outcome = await store.PostAsync(request, employeeId, context.UserId, ct).ConfigureAwait(false);
        if (!outcome.Replayed)
        {
            await PublishAsync(request.CompanyId, request.SourceModule, request.SourceDocumentType, request.SourceDocumentId, request.SourceDocumentNumber,
                request.MovementDate, request.AccountingDate, false, outcome, ct).ConfigureAwait(false);
        }

        return outcome;
    }

    public async Task<StockPostingResult> ReverseAsync(StockReversalRequest request, CancellationToken ct) =>
        Result(await ReverseWithOutcomeAsync(request, ct).ConfigureAwait(false));

    public async Task<StockPostingOutcome> ReverseWithOutcomeAsync(StockReversalRequest request, CancellationToken ct)
    {
        if (!IsEnabled)
        {
            return new StockPostingOutcome([], false);
        }

        await EnsureDateAsync(request.CompanyId, request.MovementDate, request.MovementDate, ct).ConfigureAwait(false);
        var outcome = await store.ReverseAsync(request, context.UserId, ct).ConfigureAwait(false);
        if (!outcome.Replayed)
        {
            await PublishAsync(request.CompanyId, request.SourceModule, request.ReversalDocumentType, request.ReversalDocumentId,
                request.ReversalDocumentNumber, request.MovementDate, request.MovementDate, true, outcome, ct).ConfigureAwait(false);
        }

        return outcome;
    }

    public Task<IReadOnlyList<StockAvailabilityDto>> GetAvailabilityAsync(int companyId, int? depotId, int? warehouseId,
        IReadOnlyCollection<int> itemVariantIds, DateOnly onDate, CancellationToken ct) =>
        IsEnabled ? store.GetAvailabilityAsync(companyId, depotId, warehouseId, itemVariantIds, onDate, ct)
            : Task.FromResult<IReadOnlyList<StockAvailabilityDto>>([]);

    private async Task EnsureDateAsync(int companyId, DateOnly movementDate, DateOnly accountingDate, CancellationToken ct)
    {
        var settings = await store.GetBackdatingAsync(companyId, ct).ConfigureAwait(false);
        if (!MovementRules.IsDateAllowed(BusinessDay.Today(time), movementDate, settings.BackdatingDays,
                context.HasPermission(InventoryPermissions.MovementBackdate), settings.BackdatingWithRight))
        {
            throw new ErpException(ErpErrorKind.Forbidden, InventoryErrorCodes.BackdateNotAllowed,
                $"Mișcarea nu poate fi datată {movementDate:dd.MM.yyyy}: limita firmei e de {settings.BackdatingDays} zile în urmă, iar data din viitor nu e permisă.");
        }

        if (await periods.IsClosedAsync(companyId, accountingDate, ct).ConfigureAwait(false))
        {
            throw ErpException.Conflict(CoreErrorCodes.PeriodClosed, $"Luna datei {accountingDate:dd.MM.yyyy} este închisă.");
        }
    }

    private async Task PublishAsync(int companyId, string module, string documentType, long documentId, string? number, DateOnly movementDate,
        DateOnly accountingDate, bool reversal, StockPostingOutcome outcome, CancellationToken ct)
    {
        await outbox.EnqueueAsync(new MiscareStocValidata(module, documentType, documentId, number, movementDate, accountingDate, reversal,
            outcome.Movements.Select(m => new StockMovementEventLine(m.MovementId, m.WarehouseId, m.DepotId, m.ItemVariantId, m.QtyBase, m.UnitCost,
                m.CostAmount, m.MovementTypeCode, m.ReasonCode, m.IsForcedNegative)).ToList()) { CompanyId = companyId }, ct).ConfigureAwait(false);
        foreach (var forced in outcome.Movements.Where(m => m.IsForcedNegative))
        {
            await outbox.EnqueueAsync(new StocNegativFortat(forced.WarehouseId, forced.ItemVariantId, forced.MovementId, module, documentId)
            {
                CompanyId = companyId,
            }, ct).ConfigureAwait(false);
        }
    }

    private static StockPostingResult Result(StockPostingOutcome outcome) =>
        new(outcome.Movements.Select(m => new PostedMovementDto(m.MovementId, m.SourceLineId, m.QtyBase, m.UnitCost, m.CostAmount, m.IsForcedNegative))
            .ToList());
}
