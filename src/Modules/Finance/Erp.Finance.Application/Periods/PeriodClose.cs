using System.Text.Json;
using Erp.Core.Contracts;
using Erp.Finance.Contracts.Periods;
using FluentValidation;

namespace Erp.Finance.Application.Periods;

/// <summary>Un pas al unei rulari (macheta c3-02): rezultatul, problemele si trecerea peste el.</summary>
public sealed record CloseStepDto(string StepCode, string Name, CloseCheckSeverity Severity, bool AllowOverride, CloseCheckOutcome Outcome, string Summary,
    IReadOnlyList<CloseCheckIssue> Issues, string? OverrideReason, string? OverriddenByName, DateTime? OverriddenAt);

/// <summary>Rularea listei de verificari a unei luni (C3 3.3, 4.1): pasii si daca luna se poate inchide.</summary>
public sealed record CloseRunDto(long Id, int PeriodId, bool CanClose, DateTime StartedAt, string? StartedByName, IReadOnlyList<CloseStepDto> Steps);

public sealed record CloseStepRecord(string StepCode, CloseCheckSeverity Severity, bool AllowOverride, CloseCheckOutcome Outcome, string Summary,
    IReadOnlyList<CloseCheckIssue> Issues, string? OverrideReason, int? OverriddenBy);

/// <summary>Un document din coada „De contat” care explica o diferenta de concordanta (C3 3.4).</summary>
public sealed record ReconciliationExplanationDto(long QueueId, string DocumentNo, byte Reason, string Amount);

/// <summary>O diferenta de concordanta (macheta c3-03). Area: 1 clienti, 2 furnizori, 3 casa, 4 stocuri; KeyId e partenerul, casa sau gestiunea.</summary>
public sealed record ReconciliationRowDto(byte Area, int KeyId, string? KeyName, string Ledger, string Subledger, string Difference,
    IReadOnlyList<ReconciliationExplanationDto> Explanations);

/// <summary>Lista de verificari prin fin.usp_PeriodCloseRun_*, fin.usp_AccountingPeriod_Get, fin.usp_PeriodCloseStep_Active (NET-06).</summary>
public interface IPeriodCloseStore
{
    Task<(DateOnly Start, DateOnly End, PeriodState Status)?> PeriodAsync(int companyId, int periodId, CancellationToken ct);

    Task<IReadOnlySet<string>> ActiveStepsAsync(CancellationToken ct);

    Task<long> SaveRunAsync(int companyId, int periodId, bool canClose, IReadOnlyList<CloseStepRecord> steps, int userId, CancellationToken ct);

    Task<CloseRunDto?> LatestAsync(int companyId, int periodId, CancellationToken ct);

    Task OverrideAsync(int companyId, long runId, string stepCode, string reason, int userId, CancellationToken ct);

    Task<IReadOnlyList<ReconciliationRowDto>> ReconciliationAsync(int companyId, DateOnly asOf, CancellationToken ct);

    Task<ClosingWrite> GenerateClosingAsync(int companyId, int periodId, ClosingKind kind, bool annualOnly, int userId, CancellationToken ct);

    Task<IReadOnlyList<ClosingLineDto>> PreviewClosingAsync(int companyId, int periodId, ClosingKind kind, bool annualOnly, CancellationToken ct);

    Task<Accounting.JournalEntryHeader> HeaderAsync(long entryId, CancellationToken ct);

    Task<bool> AnnualOnlyAsync(int companyId, CancellationToken ct);
}

/// <summary>Felul notei de inchidere (C3 3.5): regularizarea TVA sau inchiderea veniturilor si cheltuielilor in 121.</summary>
public enum ClosingKind : byte
{
    VatSettlement = 1,
    ProfitAndLoss = 2,
}

/// <summary>Rezultatul scrierii unei note de inchidere: nota (null daca nu era nimic de inchis), noua sau rescrisa (versiunea veche si totalul).</summary>
public sealed record ClosingWrite(long? EntryId, bool IsNew, int OldVersion, decimal OldTotal);

public sealed record ClosingLineDto(string AccountCode, string AccountName, byte Side, string AmountRon);

/// <summary>Notele de inchidere ale unei luni in simulare (macheta c3-04): regularizarea TVA si inchiderea in 121, fara scriere.</summary>
public sealed record ClosingPreviewDto(IReadOnlyList<ClosingLineDto> VatSettlement, IReadOnlyList<ClosingLineDto> ProfitAndLoss);

/// <summary>
/// Notele de inchidere la inchiderea lunii (C3 3.5, 4.1 pasul 5): regularizarea TVA si inchiderea claselor 6 si 7 in 121, scrise inainte de
/// trecerea lunii in Inchisa; la reinchiderea dupa o redeschidere se rescrie aceeasi nota (C3-A7). Setarea Fin.Closing.AnnualOnly inchide
/// clasele 6 si 7 doar in ultima luna a anului (decizia 5).
/// </summary>
public sealed class ClosingEntries(IPeriodCloseStore store, IOutbox outbox)
{
    public const string AnnualOnlySetting = "Fin.Closing.AnnualOnly";

    public async Task WriteAsync(int companyId, int periodId, int userId, CancellationToken ct)
    {
        var annualOnly = await store.AnnualOnlyAsync(companyId, ct).ConfigureAwait(false);
        foreach (var kind in new[] { ClosingKind.VatSettlement, ClosingKind.ProfitAndLoss })
        {
            var written = await store.GenerateClosingAsync(companyId, periodId, kind, annualOnly, userId, ct).ConfigureAwait(false);
            if (written.EntryId is not { } id)
            {
                continue;
            }

            var header = await store.HeaderAsync(id, ct).ConfigureAwait(false);
            IntegrationEvent evt = written.IsNew
                ? Accounting.JournalEntryNumbers.Event(header) with { CompanyId = companyId }
                : new Contracts.Accounting.NotaContabilaRecontata(id, written.OldVersion, written.OldTotal, header.TotalDebit) { CompanyId = companyId };
            await outbox.EnqueueAsync(evt, ct).ConfigureAwait(false);
        }
    }

    public async Task<ClosingPreviewDto> PreviewAsync(int companyId, int periodId, CancellationToken ct)
    {
        var annualOnly = await store.AnnualOnlyAsync(companyId, ct).ConfigureAwait(false);
        return new ClosingPreviewDto(await store.PreviewClosingAsync(companyId, periodId, ClosingKind.VatSettlement, annualOnly, ct).ConfigureAwait(false),
            await store.PreviewClosingAsync(companyId, periodId, ClosingKind.ProfitAndLoss, annualOnly, ct).ConfigureAwait(false));
    }
}

/// <summary>
/// Rularea listei de verificari (C3 4.1 pasul 3): fiecare pas activ (IPeriodCloseCheck, din modulele lui) in ordine; trecerile peste pasi ale
/// rularii anterioare se pastreaza pentru pasii care esueaza din nou. Luna se poate inchide cand niciun pas blocant nu a esuat netrecut.
/// </summary>
public sealed class PeriodCloseService(IPeriodCloseStore store, IEnumerable<IPeriodCloseCheck> checks)
{
    public async Task<CloseRunDto> RunAsync(int companyId, int periodId, int userId, CancellationToken ct)
    {
        var period = await store.PeriodAsync(companyId, periodId, ct).ConfigureAwait(false)
            ?? throw new ErpException(ErpErrorKind.NotFound, "FIN_PERIOD_NOT_FOUND", "Perioada nu există.");
        var active = await store.ActiveStepsAsync(ct).ConfigureAwait(false);
        var previous = (await store.LatestAsync(companyId, periodId, ct).ConfigureAwait(false))?.Steps
            .Where(s => s.OverrideReason is not null).ToDictionary(s => s.StepCode, StringComparer.Ordinal) ?? [];
        var steps = new List<CloseStepRecord>();
        foreach (var check in checks.Where(c => active.Contains(c.Code)))
        {
            var r = await check.RunAsync(companyId, period.Start, period.End, ct).ConfigureAwait(false);
            var carried = r.Outcome == CloseCheckOutcome.Failed && check.AllowOverride && previous.TryGetValue(check.Code, out var p) ? p : null;
            steps.Add(new CloseStepRecord(check.Code, check.Severity, check.AllowOverride, r.Outcome, r.Summary, r.Issues, carried?.OverrideReason,
                carried is null ? null : userId));
        }

        var canClose = !steps.Any(Blocks);
        await store.SaveRunAsync(companyId, periodId, canClose, steps, userId, ct).ConfigureAwait(false);
        return (await store.LatestAsync(companyId, periodId, ct).ConfigureAwait(false))!;
    }

    public static bool Blocks(CloseStepRecord s) => s.Severity == CloseCheckSeverity.Blocking && s.Outcome == CloseCheckOutcome.Failed && s.OverrideReason is null;

    /// <summary>Eroarea inchiderii refuzate, cu codul primului pas blocant (C3 7): luna anterioara, evenimentele in tranzit, maparea, altfel lista.</summary>
    public static ErpException Refusal(CloseRunDto run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var first = run.Steps.First(s => s.Severity == CloseCheckSeverity.Blocking && s.Outcome == CloseCheckOutcome.Failed && s.OverrideReason is null);
        var code = first.StepCode switch
        {
            "PREV_PERIOD" => "FIN_PERIOD_PREVIOUS_OPEN",
            "EVENTS_IN_TRANSIT" => "FIN_PERIOD_EVENTS_IN_TRANSIT",
            "ALT_MAPPING" => "FIN_MAPPING_INCOMPLETE",
            "COST_RECALC_PENDING" => "INV_RECALC_PENDING",
            _ => "FIN_PERIOD_CLOSE_BLOCKED",
        };
        return new ErpException(ErpErrorKind.Conflict, code, $"Luna nu se poate închide: {first.Name} — {first.Summary}");
    }

    internal static string IssuesJson(IReadOnlyList<CloseCheckIssue> issues) => JsonSerializer.Serialize(issues, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>Ultima rulare a listei pentru o luna (macheta c3-02); null daca lista nu a rulat inca.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodView)]
public sealed record GetPeriodCloseRun(int PeriodId) : IQuery<CloseRunDto?>;

/// <summary>Ruleaza lista de verificari a unei luni (C3 4.1 pasul 3), oricand in luna, ca verificare anticipata.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodSoftClose)]
public sealed record RunPeriodClose(int PeriodId) : ICommand<CloseRunDto>;

/// <summary>Trece peste un pas blocant esuat, cu motiv (C3 decizia 4); coada „De contat” si evenimentele in tranzit nu permit.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodCloseOverride)]
[Audited("Fin.Period.CloseOverride")]
public sealed record OverrideCloseStep(long RunId, string StepCode, string Reason) : ICommand<bool>;

internal sealed class OverrideCloseStepValidator : AbstractValidator<OverrideCloseStep>
{
    public OverrideCloseStepValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Scrieți motivul.").MaximumLength(500);
        RuleFor(x => x.StepCode).NotEmpty().MaximumLength(40);
    }
}

/// <summary>Notele de inchidere ale unei luni in simulare (C3 3.5): se pot vedea oricand, inainte de inchidere.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodView)]
public sealed record PreviewClosingEntries(int PeriodId) : IQuery<ClosingPreviewDto>;

internal sealed class PreviewClosingEntriesHandler(ClosingEntries closing, ICurrentContext context) : IQueryHandler<PreviewClosingEntries, ClosingPreviewDto>
{
    public Task<ClosingPreviewDto> Handle(PreviewClosingEntries request, CancellationToken cancellationToken) =>
        closing.PreviewAsync(context.CompanyId, request.PeriodId, cancellationToken);
}

/// <summary>Concordantele la o data (C3 3.4, macheta c3-03): diferentele dintre contabilitate si evidenta operativa, cu documentele care le explica.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.PeriodView)]
public sealed record GetReconciliation(DateOnly AsOf) : IQuery<IReadOnlyList<ReconciliationRowDto>>;

internal sealed class PeriodCloseHandlers(PeriodCloseService service, IPeriodCloseStore store, ICurrentContext context, IAuditScope audit) :
    IQueryHandler<GetReconciliation, IReadOnlyList<ReconciliationRowDto>>, IQueryHandler<GetPeriodCloseRun, CloseRunDto?>, ICommandHandler<RunPeriodClose, CloseRunDto>, ICommandHandler<OverrideCloseStep, bool>
{
    public Task<IReadOnlyList<ReconciliationRowDto>> Handle(GetReconciliation request, CancellationToken cancellationToken) =>
        store.ReconciliationAsync(context.CompanyId, request.AsOf, cancellationToken);

    public Task<CloseRunDto?> Handle(GetPeriodCloseRun request, CancellationToken cancellationToken) =>
        store.LatestAsync(context.CompanyId, request.PeriodId, cancellationToken);

    public Task<CloseRunDto> Handle(RunPeriodClose request, CancellationToken cancellationToken) =>
        service.RunAsync(context.CompanyId, request.PeriodId, context.UserId, cancellationToken);

    public async Task<bool> Handle(OverrideCloseStep request, CancellationToken cancellationToken)
    {
        await store.OverrideAsync(context.CompanyId, request.RunId, request.StepCode, request.Reason.Trim(), context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.PeriodCloseRun", request.RunId, new { request.StepCode, request.Reason });
        return true;
    }
}
