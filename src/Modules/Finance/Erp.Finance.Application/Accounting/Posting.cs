using Erp.Core.Contracts;
using Erp.Finance.Contracts.Accounting;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Accounting;

namespace Erp.Finance.Application.Accounting;

/// <summary>Un rol de cont in vigoare (C1 3.3), cu conditiile lui; CompanyId null = al instalarii.</summary>
public sealed record RoleRow(string Role, byte? PartnerKind, string? CurrencyCode, int? CompanyId, string Code);

/// <summary>Datele de rezolvare a conturilor unei note (C2 3.4), cum vin din fin.usp_AccountResolution_LoadContext.</summary>
public sealed record ResolutionData(IReadOnlyList<RoleRow> Roles, IReadOnlyDictionary<int, string> TreasuryAccounts,
    IReadOnlyDictionary<string, string> OperationTypeAccounts, IReadOnlyDictionary<string, string> ExpenseTypeAccounts,
    IReadOnlyDictionary<int, byte> PartnerKinds, IReadOnlyDictionary<string, byte> CurrencyModes)
{
    /// <summary>Cotele TVA ale Nucleului, pentru registrul de TVA (C4 3.1).</summary>
    public IReadOnlyList<Domain.Declarations.VatRateInfo> VatRates { get; init; } = [];

    /// <summary>
    /// Contextul motorului: rolul cel mai specific castiga (al firmei inaintea celui al instalarii, apoi tipul partenerului, apoi moneda);
    /// randurile vin deja in aceasta ordine din procedura.
    /// </summary>
    public ResolutionContext ToContext(decimal maxRoundingDiff) => new(
        (role, kind, currency) => Roles.FirstOrDefault(r => string.Equals(r.Role, role, StringComparison.OrdinalIgnoreCase)
            && (r.PartnerKind is null || r.PartnerKind == kind)
            && (r.CurrencyCode is null || string.Equals(r.CurrencyCode.Trim(), currency, StringComparison.OrdinalIgnoreCase)))?.Code,
        TreasuryAccounts, OperationTypeAccounts, ExpenseTypeAccounts,
        id => PartnerKinds.TryGetValue(id, out var k) ? k : null,
        code => CurrencyModes.TryGetValue(code.ToUpperInvariant(), out var m) ? m : (byte)0,
        maxRoundingDiff);
}

/// <summary>Rezultatul scrierii unei note automate: 0 sau codul motivului (518xx), mesajul si antetul notei scrise (sau deja existente).</summary>
public sealed record PostingInsertResult(int ErrorCode, string? ErrorMessage, JournalEntryHeader? Header);

/// <summary>Un eveniment de contat: identitatea lui (idempotenta, coada), tipul (alege modelul), firma si contractul de contare.</summary>
public sealed record PostingRequest(Guid EventId, string EventType, int CompanyId, AccountingPayload Payload);

/// <summary>Rezultatul contarii unui eveniment: nota scrisa, sau motivul pentru care evenimentul a intrat in coada.</summary>
/// <summary>Un rand din coada de reincercat: evenimentul si contractul lui pastrat.</summary>
public sealed record QueuedEvent(long Id, Guid EventId, string EventType, int CompanyId, AccountingPayload Payload);

/// <summary>Ce randuri din coada se reincearca: unul, ale unei firme pe motiv sau pe interval, ori cele vechi (jobul).</summary>
public sealed record QueueFilter(int? CompanyId = null, long? Id = null, PostingFailureReason? Reason = null, DateOnly? From = null, DateOnly? To = null,
    DateTime? OlderThan = null, bool SkipClosedPeriod = false, int Take = 500);

/// <summary>Un rand al cozii „De contat” (macheta c2-04). Status: 1 in asteptare, 2 rezolvat, 3 ignorat.</summary>
public sealed record PostingQueueItemDto(long Id, Guid EventId, string EventType, string? SourceDocumentType, long? SourceDocumentId,
    string? SourceDocumentNumber, DateOnly AccountingDate, PostingFailureReason Reason, string? Details, byte Status, int Attempts, DateTime LastAttemptAt,
    long? ResolvedEntryId, string? IgnoreReason, byte[] RowVersion);

/// <summary>O linie existenta a unei note, pe cod de cont, pentru comparatia de la recontare.</summary>
public sealed record ExistingLine(string AccountCode, byte Side, decimal AmountRon, int? PartnerId, int? EmployeeId, int? CostCenterId, int? ProjectId,
    int? WarehouseId, int? SiteId);

/// <summary>O nota automata care se poate reconta: contractul pastrat si liniile de acum.</summary>
public sealed record RepostCandidate(long Id, string JournalCode, int Number, DateOnly AccountingDate, string EventType, AccountingPayload Payload,
    byte[] RowVersion, IReadOnlyList<ExistingLine> Lines);

public sealed record PostingOutcome(long? EntryId, PostingFailureReason? Reason, string? Details)
{
    public bool Posted => EntryId is not null;
}

/// <summary>Contarea prin fin.usp_PostingModel_Load, fin.usp_AccountResolution_LoadContext, fin.usp_JournalEntry_Insert, fin.usp_PostingQueue_* (NET-06).</summary>
public interface IPostingStore
{
    Task<PostingModelDef?> LoadModelAsync(string eventType, DateOnly onDate, CancellationToken ct);

    Task<ResolutionData> LoadContextAsync(int companyId, DateOnly onDate, IReadOnlyCollection<int> partnerIds, CancellationToken ct);

    Task<PostingInsertResult> InsertAsync(PostingRequest request, int journalId, int? variantId, IReadOnlyList<PostingLine> lines, CancellationToken ct);

    /// <summary>Nota de storno a notei documentului original (C2 4.2); ErrorCode -1 daca originalul nu are nota.</summary>
    Task<PostingInsertResult> ReverseFromEventAsync(PostingRequest request, long originalDocumentId, bool black, CancellationToken ct);

    Task<byte?> PendingReasonAsync(int companyId, string documentType, long documentId, CancellationToken ct);

    /// <summary>Pune evenimentul in coada; true la primul esec.</summary>
    Task<bool> QueueAsync(PostingRequest request, PostingFailureReason reason, string? details, CancellationToken ct);

    Task<IReadOnlyList<QueuedEvent>> DueAsync(QueueFilter filter, CancellationToken ct);

    Task<IReadOnlyList<PostingQueueItemDto>> ListQueueAsync(int companyId, byte? status, CancellationToken ct);

    Task IgnoreAsync(int companyId, long id, string reason, byte[] rowVersion, int userId, CancellationToken ct);

    Task<IReadOnlyList<RepostCandidate>> RepostCandidatesAsync(int companyId, DateOnly from, DateOnly to, string? eventType, CancellationToken ct);

    Task<(int OldVersion, decimal OldTotal, decimal NewTotal)> RepostAsync(int companyId, long entryId, byte[] rowVersion, int? variantId,
        IReadOnlyList<PostingLine> lines, string reason, int userId, CancellationToken ct);

    Task<string?> SettingAsync(int companyId, string code, CancellationToken ct);

    /// <summary>Randurile registrului de TVA ale unui document, odata cu nota (C4 3.1).</summary>
    Task WriteVatAsync(int companyId, long entryId, AccountingPayload payload, byte direction, bool vatOnCollection,
        IReadOnlyList<Domain.Declarations.VatRow> rows, CancellationToken ct);

    /// <summary>Partea exigibila a unei facturi cu TVA la incasare (C4 3.1, C4-A3).</summary>
    Task WriteExigibleVatAsync(int companyId, long entryId, string documentType, long documentId, DateOnly date, decimal baseRon, decimal vatRon,
        CancellationToken ct);
}

/// <summary>
/// Contarea automata (C2 4.1): modelul tipului de eveniment, contextul de rezolvare, motorul pur, centrul de cost implicit pe clasele 6 si 7,
/// apoi nota validata (cu NotaContabilaValidata) sau coada „De contat” cu motivul. Evenimentul nu se pierde si nu blocheaza inbox-ul: doar
/// erorile neasteptate (baza de date cazuta) arunca, iar procesatorul outbox-ului reincearca.
/// </summary>
public sealed class PostingService(IPostingStore store, IOutbox outbox)
{
    public const string ReversalStyleSetting = "Fin.Posting.ReversalStyle";
    public const string MaxRoundingSetting = "Fin.Posting.MaxRoundingDiff";
    public const string DefaultCostCenterSetting = "Fin.Posting.DefaultCostCenterId";
    public const int SystemUserId = 1;

    public async Task<PostingOutcome> PostAsync(PostingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var p = request.Payload;
        if (p.IsReversal && p.ReversedDocumentId is { } original)
        {
            // Stornarea la sursa (C2 4.2): nota de storno a notei originale, cu conturile ei, nu o contare cu modelul de azi.
            var black = string.Equals(await store.SettingAsync(request.CompanyId, ReversalStyleSetting, ct).ConfigureAwait(false), "Black",
                StringComparison.OrdinalIgnoreCase);
            var reversed = await store.ReverseFromEventAsync(request, original, black, ct).ConfigureAwait(false);
            if (reversed.ErrorCode != -1)
            {
                return await Written(request, reversed, ct).ConfigureAwait(false);
            }

            if (await store.PendingReasonAsync(request.CompanyId, p.DocumentType, original, ct).ConfigureAwait(false) is { } pending)
            {
                return await QueueAsync(request, (PostingFailureReason)pending, "Documentul stornat așteaptă și el în coadă; se rezolvă împreună.", ct)
                    .ConfigureAwait(false);
            }
        }

        var (model, result, data) = await BuildAsync(request.CompanyId, request.EventType, request.Payload, ct).ConfigureAwait(false);
        if (!result.Succeeded || model is null)
        {
            return await QueueAsync(request, result.Failure ?? PostingFailureReason.ModelMissing, result.Details, ct).ConfigureAwait(false);
        }

        var written = await store.InsertAsync(request, model.JournalId, result.VariantId, result.Lines, ct).ConfigureAwait(false);
        if (written.ErrorCode == 0)
        {
            await RegisterVatAsync(request, written.Header!.Id, data, ct).ConfigureAwait(false);
        }

        return await Written(request, written, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Registrul de TVA odata cu nota (C4 3.1, decizia 2): facturile de vanzare si de furnizor scriu randurile pe cota si tip de operatiune; TVA
    /// la incasare devenita exigibila scrie partea exigibila a facturii. Stornarea la sursa le copiaza cu semn schimbat (in SQL).
    /// </summary>
    private async Task RegisterVatAsync(PostingRequest request, long entryId, ResolutionData data, CancellationToken ct)
    {
        var p = request.Payload;
        if (request.EventType == "TvaLaIncasareExigibil")
        {
            await store.WriteExigibleVatAsync(request.CompanyId, entryId, p.DocumentType, p.DocumentId, p.AccountingDate,
                p.Components.Where(c => c.Component == "Base").Sum(c => c.AmountRon), p.Components.Where(c => c.Component == "Vat").Sum(c => c.AmountRon), ct)
                .ConfigureAwait(false);
            return;
        }

        byte direction = request.EventType switch
        {
            "FacturaEmisa" => Domain.Declarations.VatRegister.Sales,
            "FacturaFurnizorValidata" => Domain.Declarations.VatRegister.Purchases,
            _ => 0,
        };
        if (direction == 0)
        {
            return;
        }

        var kind = p.PartnerId is { } partner && data.PartnerKinds.TryGetValue(partner, out var k) ? k : (byte?)null;
        var rows = Domain.Declarations.VatRegister.Build(direction, p.Flags, kind, p.Components, p.AccountingDate, data.VatRates);
        if (rows.Count > 0)
        {
            await store.WriteVatAsync(request.CompanyId, entryId, p, direction, p.Flags.Contains("VatOnCollection", StringComparer.OrdinalIgnoreCase), rows, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Reincercarea unui rand din coada (C2 4.4), din contractul pastrat.</summary>
    public Task<PostingOutcome> RetryAsync(QueuedEvent queued, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(queued);
        return PostAsync(new PostingRequest(queued.EventId, queued.EventType, queued.CompanyId, queued.Payload), ct);
    }

    private async Task<PostingOutcome> Written(PostingRequest request, PostingInsertResult written, CancellationToken ct)
    {
        switch (written.ErrorCode)
        {
            case 0:
                await outbox.EnqueueAsync(JournalEntryNumbers.Event(written.Header!) with { CompanyId = request.CompanyId }, ct).ConfigureAwait(false);
                return new PostingOutcome(written.Header!.Id, null, null);
            case 51849:
                return new PostingOutcome(written.Header?.Id, null, null);
            default:
                return await QueueAsync(request, ReasonOf(written.ErrorCode), written.ErrorMessage, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Simularea (C2 4.2, IPostingPreview) si recontarea (4.6): aceeasi functie ca la contarea reala, fara scriere.</summary>
    public async Task<PostingResult> PreviewAsync(int companyId, string eventType, AccountingPayload payload, CancellationToken ct) =>
        (await BuildAsync(companyId, eventType, payload, ct).ConfigureAwait(false)).Result;

    /// <summary>Codul de eroare al scrierii (518xx) tradus in motivul de coada (C2 3.7).</summary>
    public static PostingFailureReason ReasonOf(int sqlError) => sqlError switch
    {
        51843 => PostingFailureReason.Unbalanced,
        51844 or 51847 => PostingFailureReason.AccountInvalid,
        51845 or 51846 => PostingFailureReason.DimensionMissing,
        51848 => PostingFailureReason.ClosedPeriod,
        _ => PostingFailureReason.Unexpected,
    };

    private async Task<(PostingModelDef? Model, PostingResult Result, ResolutionData Data)> BuildAsync(int companyId, string eventType, AccountingPayload payload, CancellationToken ct)
    {
        var model = await store.LoadModelAsync(eventType, payload.AccountingDate, ct).ConfigureAwait(false);
        var partners = payload.Components.Select(c => c.PartnerId).Append(payload.PartnerId).OfType<int>().Distinct().ToList();
        var data = await store.LoadContextAsync(companyId, payload.AccountingDate, partners, ct).ConfigureAwait(false);
        var maxDiff = Money.ParseOptional(await store.SettingAsync(companyId, MaxRoundingSetting, ct).ConfigureAwait(false)) ?? 0.05m;
        var result = PostingEngine.Build(model, payload, data.ToContext(maxDiff));
        if (result.Succeeded && int.TryParse(await store.SettingAsync(companyId, DefaultCostCenterSetting, ct).ConfigureAwait(false), out var costCenter) && costCenter > 0)
        {
            // Liniile de cheltuieli si venituri fara centru de cost (documentul nu l-a dat) primesc centrul implicit al firmei (C1 3.2).
            result = result with
            {
                Lines = result.Lines.Select(l => l.CostCenterId is null && l.AccountCode.Length > 0 && l.AccountCode[0] is '6' or '7'
                    ? l with { CostCenterId = costCenter } : l).ToList(),
            };
        }

        return (model, result, data);
    }

    private async Task<PostingOutcome> QueueAsync(PostingRequest request, PostingFailureReason reason, string? details, CancellationToken ct)
    {
        if (await store.QueueAsync(request, reason, details, ct).ConfigureAwait(false))
        {
            var p = request.Payload;
            await outbox.EnqueueAsync(new ContareEsuata(request.EventId, request.EventType, (byte)reason, p.DocumentType, p.DocumentId == 0 ? null : p.DocumentId,
                p.DocumentNumber, p.AccountingDate) { CompanyId = request.CompanyId }, ct).ConfigureAwait(false);
        }

        return new PostingOutcome(null, reason, details);
    }
}
