using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Reporting;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Reporting;

/// <summary>Balanta de grup prin fin.usp_GroupBalance_Run, _Get, fin.usp_Intercompany_Differences, fin.usp_IntercompanyPair_Save.</summary>
public sealed class GroupBalanceStore(IDbSession session) : IGroupBalanceStore
{
    public Task<int> RunAsync(int chartId, int year, int month, bool final, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_GroupBalance_Run", new
        {
            ChartId = chartId, Year = (short)year, Month = (byte)month, Final = final, UserId = userId,
        }, ct));

    public async Task<(int ChartId, string ChartCode, int Year, int Month, bool IsFinal, DateTime RunAt, IReadOnlyList<GroupCompanyDto> Companies, IReadOnlyList<GroupLine> Lines)?>
        GetAsync(int runId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_GroupBalance_Get", new { RunId = runId }, ct)).ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<(int Id, int ChartId, string ChartCode, short PeriodYear, byte PeriodMonth, bool IsFinal, DateTime RunAt)?>()
            .ConfigureAwait(false);
        var companies = (await grid.ReadAsync<GroupCompanyDto>().ConfigureAwait(false)).ToList();
        var lines = (await grid.ReadAsync<GroupLine>().ConfigureAwait(false)).ToList();
        return h is { } v ? (v.ChartId, v.ChartCode, v.PeriodYear, v.PeriodMonth, v.IsFinal, v.RunAt, companies, lines) : null;
    }

    public async Task<IReadOnlyList<IntercompanyDifferenceDto>> DifferencesAsync(int chartId, int year, int month, CancellationToken ct) =>
        (await session.Connection.QueryAsync<DifferenceRow>(Command("fin.usp_Intercompany_Differences", new { ChartId = chartId, Year = (short)year, Month = (byte)month }, ct))
            .ConfigureAwait(false)).Select(d => new IntercompanyDifferenceDto(d.PairName, d.FirstCompanyId, d.FirstCompanyCode, d.SecondCompanyId, d.SecondCompanyCode,
            Money.Format(d.FirstAmount), Money.Format(d.SecondAmount), Money.Format(d.Difference))).ToList();

    public Task<int> SavePairAsync(int chartId, string name, int firstAccountId, int secondAccountId, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_IntercompanyPair_Save", new
        {
            ChartId = chartId, Name = name, FirstAccountId = firstAccountId, SecondAccountId = secondAccountId, UserId = userId,
        }, ct));

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record DifferenceRow(string PairName, int FirstCompanyId, string FirstCompanyCode, int SecondCompanyId, string SecondCompanyCode, decimal FirstAmount,
        decimal SecondAmount, decimal Difference);
}
