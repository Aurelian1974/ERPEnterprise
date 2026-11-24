using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Accounts;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Accounts;

/// <summary>Rezultatele maparii prin fin.usp_AltMapping_Months, _Recalculate, _Unmapped, fin.usp_AltChartPeriod_SetFrozen (NET-06).</summary>
public sealed class AltMappingResultStore(IDbSession session) : IAltMappingResultStore
{
    public async Task<IReadOnlyList<AltMonthDto>> MonthsAsync(int chartId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int CompanyId, short Year, byte Month, int LineCount, bool IsFrozen)>(Command("fin.usp_AltMapping_Months", new
        {
            ChartId = chartId, From = Day(from), To = Day(to),
        }, ct)).ConfigureAwait(false)).Select(m => new AltMonthDto(m.CompanyId, m.Year, m.Month, m.LineCount, m.IsFrozen)).ToList();

    public async Task<(bool Skipped, int LineCount, int UnmappedCount)> RecalculateAsync(int chartId, int companyId, int year, int month, int userId,
        CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<(bool, int, int)>(Command("fin.usp_AltMapping_Recalculate", new
        {
            ChartId = chartId, CompanyId = companyId, PeriodYear = (short)year, PeriodMonth = (byte)month, UserId = userId,
        }, ct)).ConfigureAwait(false);

    public Task SetFrozenAsync(int chartId, int companyId, int year, int month, bool frozen, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_AltChartPeriod_SetFrozen", new
        {
            ChartId = chartId, CompanyId = companyId, PeriodYear = (short)year, PeriodMonth = (byte)month, Frozen = frozen, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<UnmappedAccountDto>> UnmappedAsync(int chartId, int companyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int AccountId, string Code, string Name, decimal Debit, decimal Credit, int LineCount)>(Command(
            "fin.usp_AltMapping_Unmapped", new { ChartId = chartId, CompanyId = companyId, From = Day(from), To = Day(to) }, ct)).ConfigureAwait(false))
        .Select(u => new UnmappedAccountDto(u.AccountId, u.Code, u.Name, Money.Format(u.Debit), Money.Format(u.Credit), u.LineCount)).ToList();

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
}
