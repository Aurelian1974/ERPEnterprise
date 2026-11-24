using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Planning;
using Erp.Finance.Domain.Planning;
using Erp.Finance.Infrastructure.Treasury;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Instantaneele, realizatul si regulile de alerta de cash-flow prin procedurile fin.usp_CashFlow* (NET-06).</summary>
public sealed class CashFlowSnapshotStore(IDbSession session) : ICashFlowSnapshotStore
{
    public Task<int> CreateAsync(int companyId, string name, int userId, DateOnly start, DateOnly from, DateOnly to, Granularity granularity,
        int? treasuryAccountId, string? currencyCode, bool isAutomatic, decimal openingBalanceRon, string parameters, IReadOnlyList<SnapshotLineRecord> lines,
        CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_CashFlowSnapshot_Create", new
        {
            CompanyId = companyId, Name = name, UserId = userId, StartDate = Day(start), HorizonFrom = Day(from), HorizonTo = Day(to),
            Granularity = (byte)granularity, TreasuryAccountId = treasuryAccountId, CurrencyCode = currencyCode, IsAutomatic = isAutomatic,
            OpeningBalanceRon = openingBalanceRon, Parameters = parameters,
            Lines = JsonSerializer.Serialize(lines.Select(l => new
            {
                periodStart = l.PeriodStart.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), categoryId = l.CategoryId,
                treasuryAccountId = l.TreasuryAccountId, currencyCode = l.CurrencyCode, amount = l.Amount, amountRon = l.AmountRon, sourceKind = (byte)l.SourceKind,
            })),
        }, ct));

    public async Task<IReadOnlyList<CashFlowSnapshotItemDto>> ListAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(Command("fin.usp_CashFlowSnapshot_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(s => new CashFlowSnapshotItemDto(s.Id, s.Name, s.TakenAt, s.TakenByName, DateOnly.FromDateTime(s.StartDate), DateOnly.FromDateTime(s.HorizonFrom),
            DateOnly.FromDateTime(s.HorizonTo), (Granularity)s.Granularity, s.TreasuryAccountId, s.TreasuryAccountCode, s.CurrencyCode?.Trim(), s.IsAutomatic,
            Erp.Finance.Domain.Money.Format(s.OpeningBalanceRon))).ToList();

    public async Task<(SnapshotHeader Header, IReadOnlyList<(DateOnly PeriodStart, int? CategoryId, decimal AmountRon)> Lines)?> GetAsync(int companyId, int id,
        CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_CashFlowSnapshot_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var h = (await grid.ReadAsync<HeaderRow>().ConfigureAwait(false)).SingleOrDefault();
        if (h is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<(DateTime PeriodStart, int? CategoryId, decimal AmountRon)>().ConfigureAwait(false))
            .Select(l => (DateOnly.FromDateTime(l.PeriodStart), l.CategoryId, l.AmountRon)).ToList();
        return (new SnapshotHeader(h.Id, h.Name, h.TakenAt, DateOnly.FromDateTime(h.StartDate), DateOnly.FromDateTime(h.HorizonFrom),
            DateOnly.FromDateTime(h.HorizonTo), (Granularity)h.Granularity, h.TreasuryAccountId, h.CurrencyCode?.Trim(), h.IsAutomatic, h.OpeningBalanceRon), lines);
    }

    public async Task<IReadOnlyList<ActualRow>> ActualsAsync(int companyId, DateOnly from, DateOnly to, int? treasuryAccountId, string? currencyCode,
        CancellationToken ct) =>
        (await session.Connection.QueryAsync<(DateTime Date, int? CategoryId, string CurrencyCode, decimal Amount, decimal? AmountRon)>(Command(
            "fin.usp_CashFlow_Actuals", new
            {
                CompanyId = companyId, From = Day(from), To = Day(to), TreasuryAccountId = treasuryAccountId, CurrencyCode = currencyCode,
            }, ct)).ConfigureAwait(false))
        .Select(a => new ActualRow(DateOnly.FromDateTime(a.Date), a.CategoryId, a.CurrencyCode.Trim(), a.Amount, a.AmountRon)).ToList();

    public async Task<IReadOnlyList<AlertRuleRow>> AlertRulesAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<RuleRow>(Command("fin.usp_CashFlowAlertRule_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(r => new AlertRuleRow(r.Id, r.TreasuryAccountId, r.TreasuryAccountCode, r.MinBalanceRon, r.HorizonDays, r.NotifyRoleId, r.NotifyRoleName,
            r.IsActive, r.LastAlertedOn is { } d ? DateOnly.FromDateTime(d) : null)).ToList();

    public Task SaveAlertRulesAsync(int companyId, IReadOnlyList<(int? Id, int? TreasuryAccountId, decimal MinBalanceRon, short HorizonDays, int? NotifyRoleId,
        bool IsActive)> rules, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_CashFlowAlertRule_Save", new
        {
            CompanyId = companyId, UserId = userId,
            Rules = JsonSerializer.Serialize(rules.Select(r => new
            {
                id = r.Id, treasuryAccountId = r.TreasuryAccountId, minBalanceRon = r.MinBalanceRon, horizonDays = r.HorizonDays, notifyRoleId = r.NotifyRoleId,
                isActive = r.IsActive,
            })),
        }, ct));

    public async Task<bool> MarkAlertedAsync(int companyId, int ruleId, DateOnly today, CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<int>(Command("fin.usp_CashFlowAlertRule_MarkAlerted", new { CompanyId = companyId, Id = ruleId, Today = Day(today) },
            ct)).ConfigureAwait(false) == 1;

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record ItemRow(int Id, string Name, DateTime TakenAt, string? TakenByName, DateTime StartDate, DateTime HorizonFrom, DateTime HorizonTo,
        byte Granularity, int? TreasuryAccountId, string? TreasuryAccountCode, string? CurrencyCode, bool IsAutomatic, decimal OpeningBalanceRon);

    private sealed record HeaderRow(int Id, string Name, DateTime TakenAt, DateTime StartDate, DateTime HorizonFrom, DateTime HorizonTo, byte Granularity,
        int? TreasuryAccountId, string? CurrencyCode, bool IsAutomatic, decimal OpeningBalanceRon);

    private sealed record RuleRow(int Id, int? TreasuryAccountId, string? TreasuryAccountCode, decimal MinBalanceRon, short HorizonDays, int? NotifyRoleId,
        string? NotifyRoleName, bool IsActive, DateTime? LastAlertedOn);
}

/// <summary>
/// Joburile de cash-flow (02.02, F5 3.6, 3.7), pe fiecare firma cu conturi incluse in prognoza, in contextul de sistem al firmei: instantaneul
/// automat de luni dimineata (Fin.Forecast.AutoSnapshot, implicit pornit; saptamanal pe 13 saptamani) si verificarea zilnica a alertelor.
/// Eroarea unei firme se jurnalizeaza si nu le opreste pe celelalte.
/// </summary>
public sealed class CashFlowJobs(IServiceScopeFactory scopes, FinanceConnection connection, ILogger<CashFlowJobs> log)
{
    public const string SnapshotJobId = "fin-cash-flow-snapshot";
    public const string AlertJobId = "fin-cash-flow-alerts";

    /// <summary>Lunea la 06:40, ora Romaniei.</summary>
    public const string SnapshotSchedule = "40 6 * * 1";

    /// <summary>Zilnic la 07:10, ora Romaniei.</summary>
    public const string AlertSchedule = "10 7 * * *";

    public async Task<int> SnapshotsAsync(CancellationToken ct)
    {
        var taken = 0;
        foreach (var company in (await CompaniesAsync(ct).ConfigureAwait(false)).Where(c => c.AutoSnapshot))
        {
            taken += await TryAsync(company.CompanyId, new TakeCashFlowSnapshot(IsAutomatic: true), ct).ConfigureAwait(false) > 0 ? 1 : 0;
        }

        log.LogInformation("Instantanee de cash-flow automate: {Count}.", taken);
        return taken;
    }

    public async Task<int> AlertsAsync(CancellationToken ct)
    {
        var sent = 0;
        foreach (var company in (await CompaniesAsync(ct).ConfigureAwait(false)).Where(c => c.HasAlertRules))
        {
            sent += await TryAsync(company.CompanyId, new CheckCashFlowAlerts(), ct).ConfigureAwait(false);
        }

        log.LogInformation("Alerte de cash-flow trimise: {Count}.", sent);
        return sent;
    }

    private async Task<IReadOnlyList<(int CompanyId, bool AutoSnapshot, bool HasAlertRules)>> CompaniesAsync(CancellationToken ct)
    {
        await using var sql = new SqlConnection(connection.ConnectionString);
        return (await sql.QueryAsync<(int, bool, bool)>(new CommandDefinition("fin.usp_CashFlow_Companies", commandType: CommandType.StoredProcedure,
            cancellationToken: ct)).ConfigureAwait(false)).ToList();
    }

    private async Task<int> TryAsync(int companyId, IRequest<int> request, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ISystemContextScope>().Enter(companyId);
            return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Jobul de cash-flow {Request} a eșuat pentru firma {CompanyId}.", request.GetType().Name, companyId);
            return 0;
        }
    }
}
