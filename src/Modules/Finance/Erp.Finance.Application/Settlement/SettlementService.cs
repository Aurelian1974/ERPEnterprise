using Erp.Core.Contracts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Application.Settlement;

// ---------- Contractul intern al modulului (F3 6): F1 si F2 apeleaza alocarea direct, in tranzactia documentului de plata ----------

/// <summary>Plata sau incasarea de inregistrat (F3 6). Amount e pozitiv; semnul partidei il da rolul si IsRefund.</summary>
public sealed record PaymentSettlementRequest(int CompanyId, int PartnerId, PartnerRole Role, string SourceDocumentType, long SourceDocumentId,
    string SourceLineKey, string DocumentNo, DateOnly DocumentDate, string CurrencyCode, decimal ExchangeRate, decimal Amount, decimal AmountRon, bool IsRefund,
    IReadOnlyList<SettlementTarget> Targets, bool ConfirmWarnings)
{
    /// <summary>Plata facuta de un angajat, din decont (tip 10); altfel incasare / plata (tip 5).</summary>
    public bool IsEmployeePayment { get; init; }

    /// <summary>Restul de dupa facturile indicate merge FIFO dupa profil; false la avansuri (raman nealocate).</summary>
    public bool AllowFifo { get; init; } = true;

    /// <summary>Modulul documentului (Finance pentru F1, F2).</summary>
    public string SourceModule { get; init; } = "Finance";
}

/// <summary>O factura indicata de plata; Amount null = pana la restul ei.</summary>
public sealed record SettlementTarget(long OpenItemId, decimal? Amount);

public sealed record PaymentSettlementResult(long PaymentOpenItemId, decimal AllocatedAmount, decimal UnallocatedAmount, IReadOnlyList<long> ClosedOpenItemIds);

/// <summary>Alocarea la plata (F3 4.2), apelata de F1 si F2 in tranzactia documentului de plata.</summary>
public interface ISettlementService
{
    Task<PaymentSettlementResult> RegisterPaymentAsync(PaymentSettlementRequest request, CancellationToken ct);

    /// <summary>Stornarea platii (F3 4.2 pasul 6): alocarile ei se desfac, iar plata se inchide cu o partida inversa. Nimic de facut daca lipseste.</summary>
    Task ReversePaymentAsync(int companyId, string sourceDocumentType, long sourceDocumentId, string? sourceLineKey, DateOnly reversalDate, CancellationToken ct);
}

// ---------- Accesul la date (NET-06) ----------

public sealed record OpenItemRecord(int CompanyId, int PartnerId, PartnerRole Role, OpenItemKind Kind, string SourceModule, string SourceDocumentType,
    long SourceDocumentId, string SourceLineKey, string DocumentNo, DateOnly DocumentDate, DateOnly AccountingDate, DateOnly DueDate, string CurrencyCode,
    decimal ExchangeRate, decimal Amount, decimal AmountRon, decimal VatAmount, decimal VatAmountRon, bool IsVatOnCollection, string? GlAccountCode,
    EInvoiceStatus EInvoiceStatus, string? EInvoiceIndexNo, int? CostCenterId, int? ProjectId, long? ReversalOfId, IReadOnlyList<InvoiceDueDate> Schedules);

public sealed record WriteOffCreated(int WriteOffId, long WriteOffOpenItemId, decimal Amount, bool ItemIsDebit);

public sealed record OpenItemCreated(long Id, bool Created, AutoAllocationMode Mode, bool IsPartnerPaymentBlocked);

public sealed record OpenItemCandidate(long Id, OpenItemKind Kind, string DocumentNo, DateOnly DocumentDate, DateOnly DueDate, string CurrencyCode, decimal Amount,
    decimal OpenAmount, bool IsDisputed, bool IsBlockedForPayment, EInvoiceStatus EInvoiceStatus, bool SpvExempt);

public sealed record AllocationRequest(long DebitOpenItemId, long CreditOpenItemId, decimal Amount, DateOnly? AllocationDate, AllocationSource Source)
{
    /// <summary>Documentul F3 care creeaza alocarea (F3 3.3): compensarea, ajustarea sau efectul comercial.</summary>
    public int? CompensationId { get; init; }

    public int? WriteOffId { get; init; }

    public int? CommercialPaperId { get; init; }

    /// <summary>
    /// Cursul incrucisat ales de operator (F4 3.3), cand partidele au monede diferite: unitati din moneda platii pentru o unitate din moneda
    /// facturii; atunci Amount e in moneda platii. Lipsa lui: cursul propus dupa regula din profilul partenerului.
    /// </summary>
    public decimal? CrossRate { get; init; }
}

/// <summary>O alocare gata de scris: cererea, data ei si calculul FxCalculator (sumele pe parti, valorile in lei, cursul, diferenta).</summary>
public sealed record PlannedAllocation(AllocationRequest Request, DateOnly AllocationDate, FxPlanRow Fx);

/// <summary>O partida blocata pentru alocare (F4 4.1), cu regula de curs incrucisat din profilul partenerului.</summary>
public sealed record FxLockedItem(FxItem Item, int PartnerId, DateOnly DocumentDate, CrossRateRule Rule, decimal MarkupPercent);

/// <summary>O diferenta de curs scrisa (sau inversata, IsReversal), pentru DiferentaCursInregistrata (F4 5.1).</summary>
public sealed record FxDifferenceMade(long Id, DateOnly DifferenceDate, byte Source, long? AllocationId, int? PartnerId, PartnerRole? Role, long? OpenItemId,
    string CurrencyCode, decimal ForeignAmount, decimal DifferenceRon, bool IsIndexed, string GlAccountCode, bool IsReversal);

public sealed record AllocationMade(long AllocationId, int PartnerId, PartnerRole Role, AllocatedOpenItem Debit, AllocatedOpenItem Credit, decimal Amount,
    decimal AmountRonDebit, decimal AmountRonCredit, DateOnly AllocationDate, AllocationSource Source)
{
    /// <summary>Suma pe partea de credit (diferita de Amount, suma pe debit, doar intre monede diferite).</summary>
    public decimal CreditAmount { get; init; } = Amount;
}

public sealed record ChangedOpenItem(long Id, int PartnerId, PartnerRole Role, OpenItemKind Kind, string SourceDocumentType, long SourceDocumentId, DateOnly? ClosedAt);

public sealed record VatCashEntryMade(long Id, long OpenItemId, string SourceDocumentType, long SourceDocumentId, byte Side, DateOnly ExigibilityDate,
    decimal BaseAmountRon, decimal VatAmountRon, bool IsReversal);

/// <summary>Rezultatul unui lot de alocari sau al unei desfaceri: alocarile, partidele inchise (sau redeschise) si TVA-ul la incasare.</summary>
public sealed record AllocationOutcome(IReadOnlyList<AllocationMade> Allocations, IReadOnlyList<ChangedOpenItem> ChangedItems, IReadOnlyList<VatCashEntryMade> Vat,
    IReadOnlyList<FxDifferenceMade>? Fx = null)
{
    public IReadOnlyList<FxDifferenceMade> FxDifferences => Fx ?? [];

    public static AllocationOutcome Empty { get; } = new([], [], []);
}

public sealed record PaymentOpenItem(long Id, int PartnerId, PartnerRole Role, OpenItemKind Kind, string SourceModule, string SourceLineKey, string DocumentNo,
    DateOnly DocumentDate, DateOnly AccountingDate, string CurrencyCode, decimal ExchangeRate, decimal Amount, decimal AmountRon, bool IsReversed);

public sealed record ActiveAllocation(long Id, long DebitOpenItemId, long CreditOpenItemId, DateOnly AllocationDate, AllocationSource Source);

public sealed record OpenItemListDto(long OpenItemId, PartnerRole Role, OpenItemKind Kind, string SourceDocumentType, long SourceDocumentId, string DocumentNo,
    DateOnly DocumentDate, DateOnly DueDate, string CurrencyCode, string Amount, string OpenAmount, bool IsDisputed, string? DisputeNote, bool IsBlockedForPayment,
    EInvoiceStatus EInvoiceStatus, bool IsVatOnCollection, string VatAmount, string VatNonExigibleRon, DateOnly? ClosedAt, byte[] RowVersion);

/// <summary>Un partener pe rol in lista soldurilor (macheta f3-01); sumele in lei, cu semnul conventiei (debit +, credit −).</summary>
public sealed record PartnerBalanceRowDto(int PartnerId, string PartnerCode, string PartnerName, string? FiscalCode, PartnerRole Role, bool HasOtherRole,
    string BalanceRon, string OverdueRon, string CurrentRon, string Days30Ron, string Days60Ron, string Over60Ron, int MaxOverdueDays, string UnallocatedRon,
    int UnallocatedCount);

/// <summary>Totalurile firmei in lista soldurilor (cartonasele din f3-01); creantele si datoriile ca sume pozitive.</summary>
public sealed record PartnerBalanceTotalsDto(string ReceivablesRon, int CustomersWithBalance, string OverdueReceivablesRon, int CustomersOverdue, string PayablesRon,
    int SuppliersWithBalance, string UnallocatedRon, int UnallocatedCount, string VatNonExigibleCollectedRon, string VatNonExigibleDeductibleRon);

public sealed record PartnerBalanceListDto(DateOnly AsOf, PartnerBalanceTotalsDto Totals, IReadOnlyList<PartnerBalanceRowDto> Rows);

public sealed record PartnerBalanceFilter(DateOnly AsOf, PartnerRole? Role, string? Search, bool OnlyOverdue, bool OnlyUnallocated, bool BothRoles);

/// <summary>O zi din scadentar (macheta f3-04); Day null = restantele de dinaintea intervalului. Sumele in lei, pozitive.</summary>
public sealed record DueScheduleDayDto(DateOnly? Day, int ReceivableCount, string ReceivableRon, int PayableCount, string PayableRon);

public sealed record DueScheduleDto(DateOnly From, DateOnly To, IReadOnlyList<DueScheduleDayDto> Days);

/// <summary>O partida scadenta in detaliul zilei (f3-04), cu observatiile pentru OP: litigiu, blocari, e-Factura, ANAF, contul partenerului.</summary>
public sealed record DueItemDto(long OpenItemId, int PartnerId, string PartnerName, bool AnafInactive, string DocumentNo, DateOnly DocumentDate, DateOnly DueDate,
    string CurrencyCode, string OpenAmount, string OpenRon, bool IsDisputed, bool IsBlockedForPayment, bool PartnerBlocked, EInvoiceStatus EInvoiceStatus,
    bool SpvExempt, int? PartnerBankAccountId);

/// <summary>O factura aleasa pentru OP (F2 „Din facturi scadente”): restul de plata pozitiv si contul bancar al partenerului.</summary>
public sealed record InvoiceToPay(long OpenItemId, int PartnerId, string PartnerName, PartnerRole Role, OpenItemKind Kind, string DocumentNo, string CurrencyCode,
    decimal OpenAmount, bool IsDisputed, bool IsBlockedForPayment, bool PartnerBlocked, int? PartnerBankAccountId);

/// <summary>O alocare a partenerului (tab-ul Alocari din f3-02). Status: 1 Activa, 2 Desfacuta.</summary>
public sealed record PartnerAllocationDto(long Id, PartnerRole Role, DateOnly AllocationDate, AllocationSource Source, byte Status, string Amount, string CurrencyCode,
    long DebitOpenItemId, string DebitDocumentNo, OpenItemKind DebitKind, long CreditOpenItemId, string CreditDocumentNo, OpenItemKind CreditKind,
    DateTime? UndoneAt, string? UndoReason, DateTime CreatedAt, string? CreatedByName);

public sealed record PartnerFinanceProfileDto(int PartnerId, PartnerRole Role, AutoAllocationMode AutoAllocationMode, string PenaltyRatePerDay, int PenaltyGraceDays,
    bool PenaltyCapAtPrincipal, bool ApplyFixedRecoveryCost, bool IsPaymentBlocked, string? BlockReason, string? BalanceConfirmationEmail, byte[]? RowVersion);

public sealed record PartnerFinanceProfileRecord(int PartnerId, PartnerRole Role, AutoAllocationMode AutoAllocationMode, decimal PenaltyRatePerDay,
    int PenaltyGraceDays, bool PenaltyCapAtPrincipal, bool ApplyFixedRecoveryCost, bool IsPaymentBlocked, string? BlockReason, string? BalanceConfirmationEmail,
    byte[]? RowVersion);

public interface ISettlementStore
{
    Task<OpenItemCreated> CreateItemAsync(OpenItemRecord item, int userId, CancellationToken ct);

    /// <summary>Partidele deschise pe o parte (1 debit, 2 credit), blocate, in ordinea FIFO; ids restrange la partidele date.</summary>
    Task<IReadOnlyList<OpenItemCandidate>> CandidatesAsync(int companyId, int partnerId, PartnerRole role, string currencyCode, byte side,
        IReadOnlyCollection<long>? ids, CancellationToken ct);

    Task<AllocationOutcome> AllocateAsync(int companyId, IReadOnlyList<PlannedAllocation> allocations, int userId, CancellationToken ct);

    /// <summary>Partidele unui lot de alocari, blocate in ordinea Id-urilor, cu restul in lei si regula de curs (F4 4.1).</summary>
    Task<IReadOnlyList<FxLockedItem>> LockForAllocationAsync(int companyId, IReadOnlyCollection<long> ids, CancellationToken ct);

    Task<AllocationOutcome> UndoAsync(int companyId, IReadOnlyCollection<long> allocationIds, string? reason, bool allowDocumentSource, DateOnly today, int userId,
        CancellationToken ct);

    Task<(IReadOnlyList<PaymentOpenItem> Items, IReadOnlyList<ActiveAllocation> Allocations)> PaymentItemsAsync(int companyId, string sourceDocumentType,
        long sourceDocumentId, string? sourceLineKey, CancellationToken ct);

    Task<IReadOnlyList<OpenItemListDto>> ListOpenAsync(int companyId, int partnerId, PartnerRole? role, CancellationToken ct, bool onlyOpen = true,
        DateOnly? from = null, DateOnly? to = null);

    Task<PartnerBalanceListDto> ListBalancesAsync(int companyId, PartnerBalanceFilter filter, CancellationToken ct);

    Task<IReadOnlyList<PartnerAllocationDto>> ListAllocationsAsync(int companyId, int partnerId, PartnerRole? role, CancellationToken ct);

    Task<IReadOnlyList<InvoiceToPay>> InvoicesToPayAsync(int companyId, IReadOnlyCollection<long> ids, CancellationToken ct);

    Task<IReadOnlyList<DueScheduleDayDto>> DueScheduleAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<IReadOnlyList<DueItemDto>> DueItemsAsync(int companyId, PartnerRole role, DateOnly? day, DateOnly? before, CancellationToken ct);

    Task<IReadOnlyList<PartnerBalanceDto>> BalanceAsync(int companyId, int partnerId, PartnerRole? role, DateOnly asOf, CancellationToken ct);

    Task<byte[]> SetFlagsAsync(int companyId, long id, bool? isDisputed, string? disputeNote, bool? isBlockedForPayment, byte[] rowVersion, int userId,
        CancellationToken ct);

    Task<PartnerFinanceProfileDto> GetProfileAsync(int companyId, int partnerId, PartnerRole role, CancellationToken ct);

    Task<byte[]> SaveProfileAsync(int companyId, PartnerFinanceProfileRecord profile, int userId, CancellationToken ct);

    /// <summary>Documentul de ajustare si partida lui de tip 9 (F3 3.6); alocarea o face apelantul.</summary>
    Task<WriteOffCreated> CreateWriteOffAsync(int companyId, long openItemId, decimal? amount, WriteOffReason reason, string? note, DateOnly documentDate,
        decimal maxAmountRon, bool allowAboveThreshold, int userId, CancellationToken ct);

    /// <summary>Partida unui document sursa (factura stornata), daca exista.</summary>
    Task<long?> FindBySourceAsync(string sourceDocumentType, long sourceDocumentId, PartnerRole role, CancellationToken ct);

    /// <summary>Tipurile de operatiuni de trezorerie: decontarea (SettlementKind) si sensul (1 intrare, 2 iesire, 3 ambele), dupa cod.</summary>
    Task<IReadOnlyDictionary<string, (byte SettlementKind, byte Direction)>> OperationTypeKindsAsync(CancellationToken ct);

    /// <summary>Data unei alocari active a firmei (pentru verificarea perioadei inainte de desfacere).</summary>
    Task<DateOnly?> AllocationDateAsync(int companyId, long allocationId, CancellationToken ct);
}

// ---------- Evenimentele F3 (5.1) ----------

internal static class SettlementEvents
{
    /// <summary>Partidele care sunt facturi, pentru FacturaAchitata.</summary>
    private static bool IsInvoice(OpenItemKind kind) => kind is OpenItemKind.Invoice or OpenItemKind.CreditNote or OpenItemKind.AdvanceInvoice or OpenItemKind.DebitNote;

    public static async Task PublishAsync(IOutbox outbox, AllocationOutcome outcome, bool isUndo, CancellationToken ct)
    {
        foreach (var a in outcome.Allocations)
        {
            await outbox.EnqueueAsync(new AlocareInregistrata(a.AllocationId, a.PartnerId, a.Role, a.Debit, a.Credit, a.Amount, a.AmountRonDebit,
                a.AmountRonCredit, a.AllocationDate, (byte)a.Source, isUndo), ct).ConfigureAwait(false);
        }

        foreach (var item in outcome.ChangedItems.Where(i => IsInvoice(i.Kind)))
        {
            await outbox.EnqueueAsync(new FacturaAchitata(item.Role, item.SourceDocumentType, item.SourceDocumentId, item.PartnerId, item.ClosedAt, isUndo), ct)
                .ConfigureAwait(false);
        }

        foreach (var v in outcome.Vat)
        {
            await outbox.EnqueueAsync(new TvaLaIncasareExigibil(v.OpenItemId, v.SourceDocumentType, v.SourceDocumentId, v.Side, v.ExigibilityDate,
                v.BaseAmountRon, v.VatAmountRon, v.IsReversal), ct).ConfigureAwait(false);
        }

        foreach (var x in outcome.FxDifferences)
        {
            await outbox.EnqueueAsync(new DiferentaCursInregistrata(x.Id, x.Source, x.DifferenceDate, x.PartnerId, x.Role, x.OpenItemId, x.AllocationId,
                x.CurrencyCode, x.ForeignAmount, x.DifferenceRon, x.IsIndexed, x.GlAccountCode, x.IsReversal), ct).ConfigureAwait(false);
        }
    }
}

internal static class SettlementErrors
{
    public static ErpException Blocked(string documentNo) =>
        ErpException.Conflict("FIN_OPEN_ITEM_BLOCKED_FOR_PAYMENT", $"Factura {documentNo} este blocată la plată.");

    public static ErpException PartnerBlocked() => ErpException.Conflict("FIN_OPEN_ITEM_BLOCKED_FOR_PAYMENT", "Furnizorul este blocat la plată.");

    public static ErpException NotInSpv(string documentNo) =>
        ErpException.Conflict("FIN_OPEN_ITEM_NOT_IN_SPV", $"Factura {documentNo} nu a fost primită în SPV; plata ei este blocată.");

    public static ErpException NotInSpvUnconfirmed(string documentNo) =>
        ErpException.Conflict("FIN_OPEN_ITEM_NOT_IN_SPV_UNCONFIRMED", $"Factura {documentNo} nu a fost primită în SPV. Confirmați plata.");

    public static ErpException DisputedUnconfirmed(string documentNo) =>
        ErpException.Conflict("FIN_OPEN_ITEM_DISPUTED_UNCONFIRMED", $"Factura {documentNo} este în litigiu. Confirmați alocarea.");

    public static ErpException NotAllocatable(long id) =>
        Invalid("targets", $"Partida {id} nu este o factură deschisă a partenerului, în moneda plății, pe partea opusă plății.");

    public static ErpValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}

/// <summary>
/// Alocarea (F3 4.1–4.3): plata noua pe facturile indicate, apoi FIFO dupa profil; factura noua pe platile nealocate (FIFO) si storno pe factura
/// stornata; verificarile de blocare, SPV si litigiu (3.8, 4.2). Totul in tranzactia apelantului.
/// </summary>
public sealed class SettlementService(ISettlementStore store, IFinanceSettings settings, IOutbox outbox, ICurrentContext context, IExchangeRateProvider rates)
    : ISettlementService
{
    public async Task<PaymentSettlementResult> RegisterPaymentAsync(PaymentSettlementRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var isCustomer = request.Role == PartnerRole.Customer;
        var sign = SettlementRules.PaymentSign(isCustomer, request.IsRefund);
        var created = await store.CreateItemAsync(new OpenItemRecord(request.CompanyId, request.PartnerId, request.Role,
            request.IsEmployeePayment ? OpenItemKind.EmployeePayment : OpenItemKind.Payment, request.SourceModule, request.SourceDocumentType,
            request.SourceDocumentId, request.SourceLineKey, request.DocumentNo, request.DocumentDate, request.DocumentDate, request.DocumentDate,
            request.CurrencyCode, request.ExchangeRate, sign * request.Amount, sign * request.AmountRon, 0, 0, false, null, EInvoiceStatus.NotApplicable, null,
            null, null, null, []), context.UserId, ct).ConfigureAwait(false);
        if (!created.Created)
        {
            // A doua inregistrare a aceleiasi plati (reluare): nimic nou de alocat.
            return new PaymentSettlementResult(created.Id, 0, request.Amount, []);
        }

        // O plata catre furnizor (nu o restituire de la el) respecta blocarile si regula SPV.
        var isSupplierPayment = !isCustomer && !request.IsRefund;
        if (isSupplierPayment && created.IsPartnerPaymentBlocked)
        {
            throw SettlementErrors.PartnerBlocked();
        }

        var side = sign > 0 ? (byte)2 : (byte)1;
        var spvRule = isSupplierPayment ? await settings.RequireSpvInvoiceAsync(request.CompanyId, ct).ConfigureAwait(false) : 0;
        var plan = new List<(long Id, decimal Amount)>();
        var available = request.Amount;
        if (request.Targets.Count > 0)
        {
            var targets = await store.CandidatesAsync(request.CompanyId, request.PartnerId, request.Role, request.CurrencyCode, side,
                request.Targets.Select(t => t.OpenItemId).ToArray(), ct).ConfigureAwait(false);
            foreach (var target in request.Targets)
            {
                var item = targets.SingleOrDefault(c => c.Id == target.OpenItemId);
                if (item is null || item.OpenAmount == 0 || Math.Sign(item.Amount) != -Math.Sign(sign))
                {
                    throw SettlementErrors.NotAllocatable(target.OpenItemId);
                }

                Check(item, isSupplierPayment, spvRule, request.ConfirmWarnings, automatic: false);
                // Intre monede diferite suma e in moneda platii; FxCalculator o limiteaza la restul facturii, la cursul incrucisat (F4 3.3).
                var amount = Math.Min(Math.Min(target.Amount ?? decimal.MaxValue, item.CurrencyCode == request.CurrencyCode ? Math.Abs(item.OpenAmount) : decimal.MaxValue),
                    available);
                if (amount > 0)
                {
                    plan.Add((item.Id, amount));
                    available -= amount;
                }
            }
        }

        if (available > 0 && request.AllowFifo && created.Mode == AutoAllocationMode.IndicatedThenFifo)
        {
            var taken = plan.Select(p => p.Id).ToHashSet();
            var fifo = (await store.CandidatesAsync(request.CompanyId, request.PartnerId, request.Role, request.CurrencyCode, side, null, ct).ConfigureAwait(false))
                .Where(c => !taken.Contains(c.Id) && IsInvoiceForFifo(c) && Allowed(c, isSupplierPayment, spvRule))
                .Select(c => new AllocationCandidate(c.Id, Math.Abs(c.OpenAmount)));
            plan.AddRange(SettlementRules.Distribute(available, fifo));
        }

        var outcome = await AllocateAsync(request.CompanyId, plan.Select(p => sign > 0
            ? new AllocationRequest(created.Id, p.Id, p.Amount, null, AllocationSource.AutomaticOnPayment)
            : new AllocationRequest(p.Id, created.Id, p.Amount, null, AllocationSource.AutomaticOnPayment)).ToList(), ct).ConfigureAwait(false);
        var allocated = outcome.Allocations.Sum(a => sign > 0 ? a.Amount : a.CreditAmount);
        return new PaymentSettlementResult(created.Id, allocated, request.Amount - allocated, outcome.ChangedItems.Select(i => i.Id).ToList());
    }

    public Task ReversePaymentAsync(int companyId, string sourceDocumentType, long sourceDocumentId, string? sourceLineKey, DateOnly reversalDate,
        CancellationToken ct) =>
        ReverseAsync(companyId, sourceDocumentType, sourceDocumentId, sourceLineKey, reversalDate, false, ct);

    /// <summary>
    /// Anularea partidelor unui document F3 (efect comercial refuzat sau stornat, F3 4.5 pasul 4): ca stornarea platii, dar desface si alocarile
    /// documentului (Source = 5).
    /// </summary>
    public Task ReverseDocumentAsync(int companyId, string sourceDocumentType, long sourceDocumentId, string? sourceLineKey, DateOnly reversalDate,
        CancellationToken ct) =>
        ReverseAsync(companyId, sourceDocumentType, sourceDocumentId, sourceLineKey, reversalDate, true, ct);

    private async Task ReverseAsync(int companyId, string sourceDocumentType, long sourceDocumentId, string? sourceLineKey, DateOnly reversalDate,
        bool allowDocumentSource, CancellationToken ct)
    {
        var (items, allocations) = await store.PaymentItemsAsync(companyId, sourceDocumentType, sourceDocumentId, sourceLineKey, ct).ConfigureAwait(false);
        var pending = items.Where(i => !i.IsReversed).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var ids = pending.Select(i => i.Id).ToHashSet();
        var undo = allocations.Where(a => ids.Contains(a.DebitOpenItemId) || ids.Contains(a.CreditOpenItemId)).Select(a => a.Id).ToArray();
        if (undo.Length > 0)
        {
            var undone = await store.UndoAsync(companyId, undo, "Stornarea documentului de plată", allowDocumentSource, reversalDate, context.UserId, ct)
                .ConfigureAwait(false);
            await SettlementEvents.PublishAsync(outbox, undone, true, ct).ConfigureAwait(false);
        }

        // Plata se inchide cu partida inversa, alocata pe ea (storno), ca soldul sa ramana suma partidelor (regula 1).
        var reversals = new List<AllocationRequest>();
        foreach (var item in pending)
        {
            var key = item.SourceLineKey.Length <= 48 ? item.SourceLineKey + "#S" : item.SourceLineKey[..48] + "#S";
            var reversal = await store.CreateItemAsync(new OpenItemRecord(companyId, item.PartnerId, item.Role, item.Kind, item.SourceModule, sourceDocumentType,
                sourceDocumentId, key, item.DocumentNo, reversalDate, reversalDate, reversalDate, item.CurrencyCode, item.ExchangeRate, -item.Amount,
                -item.AmountRon, 0, 0, false, null, EInvoiceStatus.NotApplicable, null, null, null, item.Id, []), context.UserId, ct).ConfigureAwait(false);
            reversals.Add(item.Amount > 0
                ? new AllocationRequest(item.Id, reversal.Id, item.Amount, reversalDate, AllocationSource.Reversal)
                : new AllocationRequest(reversal.Id, item.Id, -item.Amount, reversalDate, AllocationSource.Reversal));
        }

        await AllocateAsync(companyId, reversals, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Factura noua (F3 4.1 pasii 3–4): storno pe factura stornata (Source 4), apoi, in modul FIFO, platile nealocate ale partenerului pe factura
    /// (cele mai vechi intai). Platile catre un furnizor blocat sau pe o factura blocata la plata nu se aloca automat.
    /// </summary>
    public async Task<AllocationOutcome> AllocateNewInvoiceAsync(int companyId, int partnerId, PartnerRole role, long invoiceId, decimal amount, string currencyCode,
        long? reversedId, AutoAllocationMode mode, bool partnerBlocked, CancellationToken ct)
    {
        var plan = new List<AllocationRequest>();
        var open = Math.Abs(amount);
        var side = amount > 0 ? (byte)2 : (byte)1;
        if (reversedId is { } original)
        {
            var target = (await store.CandidatesAsync(companyId, partnerId, role, currencyCode, side, [original], ct).ConfigureAwait(false)).SingleOrDefault();
            if (target is not null && target.OpenAmount != 0 && Math.Sign(target.Amount) == -Math.Sign(amount) && target.CurrencyCode == currencyCode)
            {
                var value = Math.Min(open, Math.Abs(target.OpenAmount));
                plan.Add(amount > 0
                    ? new AllocationRequest(invoiceId, target.Id, value, null, AllocationSource.Reversal)
                    : new AllocationRequest(target.Id, invoiceId, value, null, AllocationSource.Reversal));
                open -= value;
            }
        }

        var isSupplierInvoice = role == PartnerRole.Supplier && amount < 0;
        if (open > 0 && mode == AutoAllocationMode.IndicatedThenFifo && !(isSupplierInvoice && partnerBlocked))
        {
            var payments = (await store.CandidatesAsync(companyId, partnerId, role, currencyCode, side, null, ct).ConfigureAwait(false))
                .Where(c => c.Kind is OpenItemKind.Payment or OpenItemKind.EmployeePayment or OpenItemKind.OpeningBalance && !c.IsDisputed)
                .Select(c => new AllocationCandidate(c.Id, Math.Abs(c.OpenAmount)));
            foreach (var (paymentId, value) in SettlementRules.Distribute(open, payments))
            {
                plan.Add(amount > 0
                    ? new AllocationRequest(invoiceId, paymentId, value, null, AllocationSource.AutomaticOnPayment)
                    : new AllocationRequest(paymentId, invoiceId, value, null, AllocationSource.AutomaticOnPayment));
            }
        }

        return await AllocateAsync(companyId, plan, ct).ConfigureAwait(false);
    }

    /// <summary>Alocarea manuala (F3 4.3): verificarile de blocare, SPV si litigiu pe fiecare partida, apoi lotul.</summary>
    public async Task<AllocationOutcome> AllocateManuallyAsync(int companyId, int partnerId, PartnerRole role, IReadOnlyList<AllocationRequest> pairs,
        bool confirmWarnings, CancellationToken ct)
    {
        var ids = pairs.SelectMany(p => new[] { p.DebitOpenItemId, p.CreditOpenItemId }).Distinct().ToArray();
        var items = await store.CandidatesAsync(companyId, partnerId, role, "RON", 1, ids, ct).ConfigureAwait(false);
        if (items.Count != ids.Length)
        {
            throw SettlementErrors.Invalid("pairs", "O partidă nu este a partenerului și rolului ales.");
        }

        // La furnizor, partida de debit e plata: verificarile se fac pe facturile (credit) pe care le stinge.
        var spvRule = role == PartnerRole.Supplier ? await settings.RequireSpvInvoiceAsync(companyId, ct).ConfigureAwait(false) : 0;
        foreach (var item in items.Where(i => role == PartnerRole.Supplier ? i.Amount < 0 : i.Amount > 0))
        {
            Check(item, role == PartnerRole.Supplier, spvRule, confirmWarnings, automatic: false);
        }

        return await AllocateAsync(companyId, pairs, ct).ConfigureAwait(false);
    }

    internal async Task<AllocationOutcome> AllocateAsync(int companyId, IReadOnlyList<AllocationRequest> allocations, CancellationToken ct)
    {
        if (allocations.Count == 0)
        {
            return AllocationOutcome.Empty;
        }

        var planned = await PlanAsync(companyId, allocations, ct).ConfigureAwait(false);
        var outcome = await store.AllocateAsync(companyId, planned, context.UserId, ct).ConfigureAwait(false);
        await SettlementEvents.PublishAsync(outbox, outcome, false, ct).ConfigureAwait(false);
        return outcome;
    }

    /// <summary>
    /// Diferentele de curs (F4 4.1): partidele se blocheaza, data alocarii lipsa devine data documentului mai recent (F3, regula 4), cursul
    /// incrucisat lipsa se propune dupa regula din profilul partenerului facturii (F4 3.3), apoi FxCalculator imparte valorile in lei.
    /// </summary>
    private async Task<IReadOnlyList<PlannedAllocation>> PlanAsync(int companyId, IReadOnlyList<AllocationRequest> allocations, CancellationToken ct)
    {
        var ids = allocations.SelectMany(a => new[] { a.DebitOpenItemId, a.CreditOpenItemId }).Distinct().ToArray();
        var locked = (await store.LockForAllocationAsync(companyId, ids, ct).ConfigureAwait(false)).ToDictionary(i => i.Item.Id);
        if (locked.Count != ids.Length)
        {
            throw ErpException.NotFound("FIN_INVALID_REFERENCE", "Partida nu există în firma curentă.");
        }

        var dates = new List<DateOnly>(allocations.Count);
        var requests = new List<FxRequest>(allocations.Count);
        foreach (var a in allocations)
        {
            var debit = locked[a.DebitOpenItemId];
            var credit = locked[a.CreditOpenItemId];
            var date = a.AllocationDate ?? (debit.DocumentDate >= credit.DocumentDate ? debit.DocumentDate : credit.DocumentDate);
            dates.Add(date);
            if (debit.Item.CurrencyCode == credit.Item.CurrencyCode)
            {
                requests.Add(new FxRequest(a.DebitOpenItemId, a.CreditOpenItemId, a.Amount));
                continue;
            }

            if (a.CrossRate is { } chosen)
            {
                requests.Add(new FxRequest(a.DebitOpenItemId, a.CreditOpenItemId, a.Amount, chosen, CrossRateRule.Manual));
                continue;
            }

            var invoiceIsDebit = FxCalculator.InvoiceIsDebit(debit.Item, credit.Item);
            var invoice = invoiceIsDebit ? debit : credit;
            var payment = invoiceIsDebit ? credit : debit;
            var rule = invoice.Rule == CrossRateRule.Manual ? CrossRateRule.BnrOnPaymentDate : invoice.Rule;
            var invoicePerUnit = await PerUnitAsync(invoice.Item.CurrencyCode, date, ct).ConfigureAwait(false);
            var paymentPerUnit = await PerUnitAsync(payment.Item.CurrencyCode, date, ct).ConfigureAwait(false);
            var paymentOnInvoiceDate = rule == CrossRateRule.InvoiceRate
                ? await PerUnitAsync(payment.Item.CurrencyCode, invoice.DocumentDate, ct).ConfigureAwait(false)
                : paymentPerUnit;
            var rate = FxCalculator.CrossRate(rule, invoicePerUnit, paymentPerUnit, invoice.MarkupPercent, invoice.Item.ExchangeRate, paymentOnInvoiceDate);
            requests.Add(new FxRequest(a.DebitOpenItemId, a.CreditOpenItemId, a.Amount, rate, rule));
        }

        var plan = FxCalculator.Plan(locked.ToDictionary(i => i.Key, i => i.Value.Item), requests);
        return allocations.Select((a, i) => new PlannedAllocation(a, dates[i], plan[i])).ToList();
    }

    private async Task<decimal> PerUnitAsync(string currency, DateOnly date, CancellationToken ct) =>
        currency == "RON" ? 1m : (await rates.GetRateAsync(currency, date, ct).ConfigureAwait(false)).PerUnit;

    private static bool IsInvoiceForFifo(OpenItemCandidate c) =>
        c.Kind is OpenItemKind.Invoice or OpenItemKind.AdvanceInvoice or OpenItemKind.DebitNote or OpenItemKind.OpeningBalance && !c.IsDisputed;

    private static bool Allowed(OpenItemCandidate c, bool isSupplierPayment, int spvRule) =>
        !isSupplierPayment || (!c.IsBlockedForPayment && (spvRule == 0 || c.SpvExempt || c.EInvoiceStatus != EInvoiceStatus.NotReceivedFromSpv));

    /// <summary>Blocarile si avertizarile pe o factura indicata (F3 4.2, 3.8, 4.10).</summary>
    private static void Check(OpenItemCandidate item, bool isSupplierPayment, int spvRule, bool confirmed, bool automatic)
    {
        if (isSupplierPayment && item.IsBlockedForPayment)
        {
            throw SettlementErrors.Blocked(item.DocumentNo);
        }

        if (isSupplierPayment && spvRule > 0 && !item.SpvExempt && item.EInvoiceStatus == EInvoiceStatus.NotReceivedFromSpv)
        {
            if (spvRule == 1)
            {
                throw SettlementErrors.NotInSpv(item.DocumentNo);
            }

            if (!confirmed)
            {
                throw SettlementErrors.NotInSpvUnconfirmed(item.DocumentNo);
            }
        }

        if (item.IsDisputed && (automatic || !confirmed))
        {
            throw SettlementErrors.DisputedUnconfirmed(item.DocumentNo);
        }
    }
}
