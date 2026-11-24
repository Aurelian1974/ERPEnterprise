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

    Task QueueAsync(PostingRequest request, PostingFailureReason reason, string? details, CancellationToken ct);

    Task<string?> SettingAsync(int companyId, string code, CancellationToken ct);
}

/// <summary>
/// Contarea automata (C2 4.1): modelul tipului de eveniment, contextul de rezolvare, motorul pur, centrul de cost implicit pe clasele 6 si 7,
/// apoi nota validata (cu NotaContabilaValidata) sau coada „De contat” cu motivul. Evenimentul nu se pierde si nu blocheaza inbox-ul: doar
/// erorile neasteptate (baza de date cazuta) arunca, iar procesatorul outbox-ului reincearca.
/// </summary>
public sealed class PostingService(IPostingStore store, IOutbox outbox)
{
    public const string MaxRoundingSetting = "Fin.Posting.MaxRoundingDiff";
    public const string DefaultCostCenterSetting = "Fin.Posting.DefaultCostCenterId";
    public const int SystemUserId = 1;

    public async Task<PostingOutcome> PostAsync(PostingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (model, result) = await BuildAsync(request.CompanyId, request.EventType, request.Payload, ct).ConfigureAwait(false);
        if (!result.Succeeded || model is null)
        {
            return await QueueAsync(request, result.Failure ?? PostingFailureReason.ModelMissing, result.Details, ct).ConfigureAwait(false);
        }

        var written = await store.InsertAsync(request, model.JournalId, result.VariantId, result.Lines, ct).ConfigureAwait(false);
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

    /// <summary>Simularea (C2 4.2, IPostingPreview): aceeasi functie ca la contarea reala, fara scriere.</summary>
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

    private async Task<(PostingModelDef? Model, PostingResult Result)> BuildAsync(int companyId, string eventType, AccountingPayload payload, CancellationToken ct)
    {
        var model = await store.LoadModelAsync(eventType, payload.AccountingDate, ct).ConfigureAwait(false);
        var partners = payload.Components.Select(c => c.PartnerId).Append(payload.PartnerId).OfType<int>().Distinct().ToList();
        var data = await store.LoadContextAsync(companyId, payload.AccountingDate, partners, ct).ConfigureAwait(false);
        var maxDiff = Money.ParseOptional(await store.SettingAsync(companyId, MaxRoundingSetting, ct).ConfigureAwait(false)) ?? 0.05m;
        var result = PostingEngine.Build(model, payload, data.ToContext(maxDiff));
        if (result.Succeeded && int.TryParse(await store.SettingAsync(companyId, DefaultCostCenterSetting, ct).ConfigureAwait(false), out var costCenter))
        {
            // Liniile de cheltuieli si venituri fara centru de cost (documentul nu l-a dat) primesc centrul implicit al firmei (C1 3.2).
            result = result with
            {
                Lines = result.Lines.Select(l => l.CostCenterId is null && l.AccountCode.Length > 0 && l.AccountCode[0] is '6' or '7'
                    ? l with { CostCenterId = costCenter } : l).ToList(),
            };
        }

        return (model, result);
    }

    private async Task<PostingOutcome> QueueAsync(PostingRequest request, PostingFailureReason reason, string? details, CancellationToken ct)
    {
        await store.QueueAsync(request, reason, details, ct).ConfigureAwait(false);
        return new PostingOutcome(null, reason, details);
    }
}
