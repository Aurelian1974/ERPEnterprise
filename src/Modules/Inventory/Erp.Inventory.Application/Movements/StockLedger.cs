using Erp.Core.Contracts;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;

namespace Erp.Inventory.Application.Movements;

/// <summary>Rezultatul unei inregistrari: miscarile si daca apelul a fost o reluare (sursa deja inregistrata, S2 regula 6).</summary>
public sealed record StockPostingOutcome(IReadOnlyList<PostedStockMovement> Movements, bool Replayed);

public sealed record PostedStockMovement(long MovementId, long SourceLineId, byte SourceRole, int WarehouseId, int? DepotId, int ItemVariantId, decimal QtyBase,
    decimal UnitCost, decimal CostAmount, bool IsForcedNegative, string MovementTypeCode, string? ReasonCode)
{
    /// <summary>Gestiunile la pret cu amanuntul (S3 3.7): valoarea cu amanuntul (cu TVA), TVA-ul inclus si metoda de descarcare.</summary>
    public decimal? RetailAmount { get; init; }

    public decimal? RetailVatAmount { get; init; }

    public byte? RetailDischargeMethod { get; init; }

    public int? CostCenterId { get; init; }

    /// <summary>Lotul miscarii (S4): Id-ul, codul, CKU-ul si termenul.</summary>
    public int? LotId { get; init; }

    public string? LotCode { get; init; }

    public string? CkuCode { get; init; }

    public Guid? CkuUid { get; init; }

    public DateOnly? ExpiryDate { get; init; }
}

/// <summary>
/// Loturile unui apel (inv.usp_Lot_ResolveForMovements): reluarea cu miscarile existente, felul fiecarui rand (0 fara lot, 1 intrare, 2 iesire
/// de alocat, 3 pereche sau stornare) cu lotul rezolvat, loturile cheilor cu iesiri, lotul tehnic pe varianta si regula de alegere.
/// </summary>
public sealed record LotResolution(bool Replayed, IReadOnlyList<(int Row, int? LotId, decimal Qty)> Existing,
    IReadOnlyDictionary<int, (byte Kind, int? LotId)> Rows, IReadOnlyList<Domain.Lots.LotCandidate> Candidates, IReadOnlyDictionary<int, int> TechnicalLots,
    byte PickRule);

/// <summary>Setarile de data ale firmei (S1 3.3), citite la fiecare inregistrare.</summary>
public sealed record BackdatingSettings(short BackdatingDays, bool BackdatingWithRight);

/// <summary>Accesul la date pentru registru (inv.usp_StockMovement_Post, _Reverse, inv.usp_Stock_Availability).</summary>
public interface IStockMovementStore
{
    Task<BackdatingSettings> GetBackdatingAsync(int companyId, CancellationToken ct);

    Task<LotResolution> ResolveLotsAsync(StockPostingRequest request, IReadOnlyList<PostingLine> lines, bool allowExpiredReceipt, int userId, CancellationToken ct);

    Task<CostLoad> LoadCostAsync(int companyId, IReadOnlyCollection<CostKey> keys, IReadOnlyCollection<long> reversalOf, CancellationToken ct);

    Task<StockPostingOutcome> PostAsync(StockPostingRequest request, CostedPosting costed, int? employeeId, int userId, CancellationToken ct);

    /// <summary>Miscarile de stornat ale unui document sursa (inv.usp_StockMovement_ForReversal), ca cereri cu semn opus.</summary>
    Task<IReadOnlyList<(long MovementId, StockMovementRequest Request)>> ForReversalAsync(StockReversalRequest request, CancellationToken ct);

    Task<IReadOnlyList<StockAvailabilityDto>> GetAvailabilityAsync(int companyId, int? depotId, int? warehouseId, IReadOnlyCollection<int> itemVariantIds,
        DateOnly onDate, CancellationToken ct);
}

/// <summary>
/// Portul de stoc implementat de Stocuri (02.05, S2 4.1 si 6): data miscarii (BackdatingDays, dreptul Inv.Movement.Backdate, 54103), luna
/// deschisa (Nucleu, IPeriodLock), inregistrarea in tranzactia apelantului si evenimentele MiscareStocValidata si StocNegativFortat in
/// outbox, oricare ar fi sursa. Faptele din afara ERP (POS) nu trec prin verificarea datei. Fara modulul Stocuri licentiat nu scrie nimic.
/// </summary>
public sealed class StockLedger(IStockMovementStore store, ILicenseInfo license, IPeriodLock periods, ICurrentContext context, IOutbox outbox,
    TimeProvider time, CostRecalculator recalculator, Costing.LandedCostService landedCosts) : IStockLedger
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

        var lines = request.Lines.Select((l, i) => new PostingLine(i + 1, l, null, l.SourceRole == 2
            ? request.Lines.Select((x, j) => (x, j)).Where(p => p.x.SourceLineId == l.SourceLineId && p.x.SourceRole == 1).Select(p => (int?)(p.j + 1)).FirstOrDefault()
            : null)).ToList();
        var outcome = await PostCostedAsync(request, lines, employeeId, ct).ConfigureAwait(false);
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
        var originals = await store.ForReversalAsync(request, ct).ConfigureAwait(false);
        var posting = new StockPostingRequest(request.CompanyId, request.SourceModule, request.ReversalDocumentType, request.ReversalDocumentId,
            request.ReversalDocumentNumber, request.MovementDate, request.MovementDate, null, false, originals.Select(o => o.Request).ToList());
        var lines = originals.Select((o, i) => new PostingLine(i + 1, o.Request, o.MovementId, null)).ToList();
        var outcome = await PostCostedAsync(posting, lines, null, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Costul adaugat din Achizitii (S3 3.3, 4.2): documentul CA cu tintele date prin liniile sursa (pe module), repartizat dupa baza ceruta; validat
    /// imediat daca apelantul o cere, altfel ramane in ciorna pentru contabilul de stocuri.
    /// </summary>
    public async Task<int> AdjustInboundCostAsync(InboundCostAdjustmentRequest request, CancellationToken ct)
    {
        if (!IsEnabled || request.Targets.Count == 0)
        {
            return 0;
        }

        var modules = request.Targets.Select(t => t.SourceModule).Distinct().ToList();
        if (modules.Count != 1)
        {
            throw new ErpException(ErpErrorKind.Validation, "INV_LANDED_COST_TARGET_INVALID", "Liniile vizate de costul adăugat trebuie să fie din același modul.");
        }

        var saved = await landedCosts.SaveAsync(request.CompanyId, new Costing.LandedCostDraft(null, request.AccountingDate, request.AccountingDate,
            (byte)request.Kind, request.SupplierInvoiceId, null, request.AmountRon, (byte)request.Basis, null, modules[0],
            request.Targets.Select(t => (t.SourceLineId, t.ManualAmountRon)).ToList(), null), ct).ConfigureAwait(false);
        if (request.ValidateImmediately)
        {
            await landedCosts.ValidateAsync(request.CompanyId, saved.Id, null, ct).ConfigureAwait(false);
        }

        return saved.Id;
    }

    /// <summary>Starea de cost a cheilor (blocate), costul calculat de motor (S3), apoi scrierea intr-un singur apel SQL.</summary>
    private async Task<StockPostingOutcome> PostCostedAsync(StockPostingRequest request, IReadOnlyList<PostingLine> lines, int? employeeId, CancellationToken ct)
    {
        lines = await ResolveLotsAsync(request, lines, ct).ConfigureAwait(false);
        var keys = lines.Select(l => new CostKey(l.Request.WarehouseId, l.Request.DepotId, l.Request.ItemVariantId, l.Request.LotId)).Distinct().ToList();
        var load = await store.LoadCostAsync(request.CompanyId, keys, lines.Select(l => l.ReversalOfId).OfType<long>().ToList(), ct).ConfigureAwait(false);
        CostedPosting costed;
        try
        {
            costed = CostCalculator.Calculate(lines, request.MovementDate, load);
        }
        catch (Domain.Costing.CostLayerConsumedException e)
        {
            throw ErpException.Conflict("INV_COST_REVERSAL_LAYER_CONSUMED", e.Message);
        }

        var outcome = await store.PostAsync(request, costed, employeeId, context.UserId, ct).ConfigureAwait(false);
        if (!outcome.Replayed && costed.Adjustments.Count > 0)
        {
            // Regularizarea iesirilor pe minus (S3 3.4) schimba valoarea stocului: 371 fata de costul vanzarilor, pe iesirea regularizata.
            var regularized = costed.Adjustments.Select(a => new CostAdjustmentEventLine(a.Key.WarehouseId, a.Key.ItemVariantId,
                a.MovementId ?? (a.Row is { } row ? outcome.Movements[row - 1].MovementId : null), a.Amount, "Stock")).ToList();
            await outbox.EnqueueAsync(new CostStocAjustat(CostCalculator.NegativeRegularization, request.SourceModule, request.SourceDocumentType,
                request.SourceDocumentId, request.AccountingDate, regularized)
            {
                CompanyId = request.CompanyId,
                Accounting = Costing.StockAccounting.Adjustment(CostCalculator.NegativeRegularization, request.SourceDocumentType, request.SourceDocumentId,
                    request.AccountingDate, regularized),
            }, ct).ConfigureAwait(false);
        }

        if (!outcome.Replayed)
        {
            // Miscarea cu data anterioara altor miscari ale cheii reface costul ieșirilor ulterioare (S3 3.5).
            await recalculator.AfterPostingAsync(request.CompanyId, keys, request.MovementDate, ct).ConfigureAwait(false);
        }

        return outcome;
    }

    /// <summary>
    /// Loturile (02.05, S4 3.2, 3.5): intrarile primesc lotul gasit sau creat de SQL; iesirile fara lot se impart pe loturi prin FEFO
    /// (LotAllocator), cele cu lot se verifica; perechea unui transfer primeste aceleasi loturi ca iesirea ei; la reluare, aceleasi miscari.
    /// </summary>
    private async Task<IReadOnlyList<PostingLine>> ResolveLotsAsync(StockPostingRequest request, IReadOnlyList<PostingLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
        {
            return lines;
        }

        var resolution = await store.ResolveLotsAsync(request, lines, context.HasPermission(InventoryPermissions.LotReceiveExpired), context.UserId, ct)
            .ConfigureAwait(false);
        var parts = new Dictionary<int, List<(int? LotId, decimal Qty)>>();
        if (resolution.Replayed)
        {
            foreach (var e in resolution.Existing)
            {
                (parts.TryGetValue(e.Row, out var list) ? list : parts[e.Row] = []).Add((e.LotId, e.Qty));
            }
        }
        else
        {
            var demands = lines.Where(l => resolution.Rows[l.Row].Kind == 2)
                .Select(l => new Domain.Lots.LotDemand(l.Row, l.Request.WarehouseId, l.Request.DepotId, l.Request.ItemVariantId, -l.Request.QtyBase,
                    resolution.Rows[l.Row].LotId ?? l.Request.LotId, l.Request.MovementTypeCode))
                .ToList();
            IReadOnlyList<Domain.Lots.LotAllocation> allocations;
            try
            {
                allocations = Domain.Lots.LotAllocator.Allocate(demands, resolution.Candidates, resolution.TechnicalLots, request.MovementDate,
                    resolution.PickRule, request.IsExternalFact);
            }
            catch (Domain.Lots.LotAllocationException e)
            {
                throw new ErpException(ErpErrorKind.Conflict, e.Code, e.Message);
            }

            foreach (var line in lines.Where(l => l.CounterRow is null))
            {
                var (kind, lotId) = resolution.Rows[line.Row];
                parts[line.Row] = kind == 2
                    ? allocations.Where(a => a.Row == line.Row).Select(a => ((int?)a.LotId, -a.Qty)).ToList()
                    : [(kind == 0 ? null : lotId ?? line.Request.LotId, line.Request.QtyBase)];
            }
        }

        // Randurile noi: fiecare linie pe loturile ei; perechea unui transfer urmeaza, parte cu parte, loturile iesirii.
        var result = new List<PostingLine>();
        var newRows = new Dictionary<int, List<(int Row, int? LotId, decimal Qty)>>();
        foreach (var line in lines.Where(l => l.CounterRow is null))
        {
            newRows[line.Row] = [];
            foreach (var (lotId, qty) in parts.GetValueOrDefault(line.Row) ?? [(line.Request.LotId, line.Request.QtyBase)])
            {
                var row = result.Count + 1;
                result.Add(line with { Row = row, Request = line.Request with { LotId = lotId, QtyBase = qty, NewLotCode = null, CkuUid = null } });
                newRows[line.Row].Add((row, lotId, qty));
            }
        }

        foreach (var line in lines.Where(l => l.CounterRow is not null))
        {
            var counterParts = newRows.GetValueOrDefault(line.CounterRow!.Value) ?? [];
            if (counterParts.Count == 0 || (!resolution.Replayed && resolution.Rows[line.Row].Kind == 0))
            {
                result.Add(line with { Row = result.Count + 1, CounterRow = counterParts.FirstOrDefault().Row });
                continue;
            }

            foreach (var part in counterParts)
            {
                result.Add(line with { Row = result.Count + 1, CounterRow = part.Row, Request = line.Request with { LotId = part.LotId, QtyBase = -part.Qty } });
            }
        }

        return result;
    }

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
        var lines = outcome.Movements.Select(m => new StockMovementEventLine(m.MovementId, m.WarehouseId, m.DepotId, m.ItemVariantId, m.QtyBase, m.UnitCost,
            m.CostAmount, m.MovementTypeCode, m.ReasonCode, m.IsForcedNegative)
        {
            RetailAmount = m.RetailAmount, RetailVatAmount = m.RetailVatAmount, RetailDischargeMethod = m.RetailDischargeMethod, CostCenterId = m.CostCenterId,
            LotId = m.LotId, LotCode = m.LotCode, CkuCode = m.CkuCode, CkuUid = m.CkuUid, ExpiryDate = m.ExpiryDate,
        }).ToList();
        await outbox.EnqueueAsync(new MiscareStocValidata(module, documentType, documentId, number, movementDate, accountingDate, reversal, lines)
        {
            CompanyId = companyId,
            Accounting = Costing.StockAccounting.Movements(module, documentType, documentId, number, movementDate, accountingDate, reversal, lines),
        }, ct).ConfigureAwait(false);
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
