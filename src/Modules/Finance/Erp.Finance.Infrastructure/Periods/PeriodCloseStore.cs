using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Contracts.Periods;

namespace Erp.Finance.Infrastructure.Periods;

/// <summary>Lista de verificari prin fin.usp_PeriodCloseRun_*, fin.usp_AccountingPeriod_Get, fin.usp_PeriodCloseStep_Active (NET-06).</summary>
public sealed class PeriodCloseStore(IDbSession session) : IPeriodCloseStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<(DateOnly Start, DateOnly End, PeriodState Status)?> PeriodAsync(int companyId, int periodId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<(int Id, DateTime Start, DateTime End, byte Status)?>(Command("fin.usp_AccountingPeriod_Get",
            new { CompanyId = companyId, Id = periodId }, ct)).ConfigureAwait(false);
        return row is { } r ? (DateOnly.FromDateTime(r.Start), DateOnly.FromDateTime(r.End), (PeriodState)r.Status) : null;
    }

    public async Task<IReadOnlySet<string>> ActiveStepsAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<string>(Command("fin.usp_PeriodCloseStep_Active", null, ct)).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

    public async Task<long> SaveRunAsync(int companyId, int periodId, bool canClose, IReadOnlyList<CloseStepRecord> steps, int userId, CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<long>(Command("fin.usp_PeriodCloseRun_Save", new
        {
            CompanyId = companyId, PeriodId = periodId, CanClose = canClose, UserId = userId,
            Steps = JsonSerializer.Serialize(steps.Select(s => new
            {
                stepCode = s.StepCode, severity = (byte)s.Severity, allowOverride = s.AllowOverride, outcome = (byte)s.Outcome, summary = s.Summary,
                issues = s.Issues.Count == 0 ? null : JsonSerializer.Serialize(s.Issues.Take(200), Json), overrideReason = s.OverrideReason, overriddenBy = s.OverriddenBy,
            })),
        }, ct)).ConfigureAwait(false);

    public async Task<CloseRunDto?> LatestAsync(int companyId, int periodId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PeriodCloseRun_Latest", new { CompanyId = companyId, PeriodId = periodId }, ct))
            .ConfigureAwait(false);
        var run = (await grid.ReadAsync<(long Id, int PeriodId, bool CanClose, DateTime StartedAt, string? StartedByName)>().ConfigureAwait(false)).ToList();
        var steps = (await grid.ReadAsync<StepRow>().ConfigureAwait(false)).Select(s => new CloseStepDto(s.StepCode, s.Name, (CloseCheckSeverity)s.Severity,
            s.AllowOverride, (CloseCheckOutcome)s.Outcome, s.Summary, s.Issues is null ? [] : JsonSerializer.Deserialize<List<CloseCheckIssue>>(s.Issues, Json) ?? [],
            s.OverrideReason, s.OverriddenByName, s.OverriddenAt)).ToList();
        return run.Count == 0 ? null : new CloseRunDto(run[0].Id, run[0].PeriodId, run[0].CanClose, run[0].StartedAt, run[0].StartedByName, steps);
    }

    public Task OverrideAsync(int companyId, long runId, string stepCode, string reason, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_PeriodCloseRunStep_Override", new
        {
            CompanyId = companyId, RunId = runId, StepCode = stepCode, Reason = reason, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<ReconciliationRowDto>> ReconciliationAsync(int companyId, DateOnly asOf, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_Reconciliation_Get", new
        {
            CompanyId = companyId, AsOf = asOf.ToDateTime(TimeOnly.MinValue), Explain = true,
        }, ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<(byte Area, int KeyId, string? KeyName, decimal Ledger, decimal Subledger, decimal Difference)>().ConfigureAwait(false)).ToList();
        var explain = (await grid.ReadAsync<(byte Area, int KeyId, long QueueId, string DocumentNo, byte Reason, decimal Amount)>().ConfigureAwait(false))
            .ToLookup(e => (e.Area, e.KeyId));
        return rows.Select(r => new ReconciliationRowDto(r.Area, r.KeyId, r.KeyName, Domain.Money.Format(r.Ledger), Domain.Money.Format(r.Subledger),
            Domain.Money.Format(r.Difference), explain[(r.Area, r.KeyId)].Select(e => new ReconciliationExplanationDto(e.QueueId, e.DocumentNo, e.Reason,
                Domain.Money.Format(e.Amount))).ToList())).ToList();
    }

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record StepRow(string StepCode, string Name, byte Severity, bool AllowOverride, byte Outcome, string Summary, string? Issues,
        string? OverrideReason, string? OverriddenByName, DateTime? OverriddenAt);
}

/// <summary>
/// Pasii listei de verificari scrisi in SQL (fin.usp_PeriodClose_Check): fiecare intoarce problemele gasite; fara probleme, pasul e in regula.
/// Luna anterioara, evenimentele in tranzit si coada „De contat” nu permit trecerea peste ele (C3 decizia 4).
/// </summary>
public sealed class SqlPeriodCloseCheck(IDbSession session, string code, CloseCheckSeverity severity, bool allowOverride, string okSummary, string failSummary)
    : IPeriodCloseCheck
{
    public string Code => code;

    public CloseCheckSeverity Severity => severity;

    public bool AllowOverride => allowOverride;

    public async Task<CloseCheckResult> RunAsync(int companyId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        var issues = (await session.Connection.QueryAsync<CloseCheckIssue>(new CommandDefinition("fin.usp_PeriodClose_Check", new
        {
            CompanyId = companyId, Code = code, Start = periodStart.ToDateTime(TimeOnly.MinValue), End = periodEnd.ToDateTime(TimeOnly.MinValue),
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false)).ToList();
        return issues.Count == 0
            ? new CloseCheckResult(CloseCheckOutcome.Ok, okSummary, [])
            : new CloseCheckResult(CloseCheckOutcome.Failed, string.Format(System.Globalization.CultureInfo.InvariantCulture, failSummary, issues.Count), issues);
    }

    /// <summary>Pasii livrati de C1, C2, C3 si F1, cu gravitatea si trecerea lor (C3 3.3).</summary>
    public static IEnumerable<(string Code, CloseCheckSeverity Severity, bool AllowOverride, string Ok, string Fail)> Shipped { get; } =
    [
        ("PREV_PERIOD", CloseCheckSeverity.Blocking, false, "Luna anterioară e închisă.", "Luna anterioară nu e închisă."),
        ("EVENTS_IN_TRANSIT", CloseCheckSeverity.Blocking, false, "Toate documentele validate au ajuns la contare.", "{0} documente validate nu au ajuns încă la contare."),
        ("CASH_DAYS", CloseCheckSeverity.Blocking, true, "Toate zilele de casă sunt închise.", "{0} zile de casă deschise."),
        ("POSTING_QUEUE", CloseCheckSeverity.Blocking, false, "Coada „De contat” e goală pentru lună.", "{0} documente așteaptă în coada „De contat”."),
        ("DRAFT_ENTRIES", CloseCheckSeverity.Blocking, true, "Nicio ciornă de notă în lună.", "{0} ciorne de note în lună."),
        ("RECONCILIATION", CloseCheckSeverity.Blocking, true, "Concordanțele nu au diferențe.", "{0} diferențe de concordanță."),
        ("ALT_MAPPING", CloseCheckSeverity.Blocking, false, "Planurile cu mapare completă nu au linii nemapate.", "{0} planuri alternative au linii nemapate."),
        ("ABNORMAL_BALANCES", CloseCheckSeverity.Warning, false, "Niciun sold pe partea greșită.", "{0} conturi cu sold pe partea greșită."),
    ];
}
