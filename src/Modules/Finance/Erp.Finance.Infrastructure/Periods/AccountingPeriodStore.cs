using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Periods;
using Erp.Finance.Infrastructure.Treasury;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Erp.Finance.Infrastructure.Periods;

/// <summary>Anii financiari si lunile contabile prin fin.usp_FiscalYear_*, fin.usp_AccountingPeriod_* (NET-06).</summary>
public sealed class AccountingPeriodStore(IDbSession session) : IAccountingPeriodStore
{
    public Task<int> CreateYearAsync(int companyId, int year, DateOnly start, DateOnly end, byte vatPeriodicity, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_FiscalYear_Create", new
        {
            CompanyId = companyId, Year = (short)year, StartDate = Day(start), EndDate = Day(end), VatPeriodicity = vatPeriodicity, UserId = userId,
        }, ct));

    public async Task<IReadOnlyList<FiscalYearDto>> ListAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_AccountingPeriod_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false);
        var years = (await grid.ReadAsync<YearRow>().ConfigureAwait(false)).ToList();
        var periods = (await grid.ReadAsync<PeriodRow>().ConfigureAwait(false)).ToLookup(p => p.FiscalYearId);
        return years.Select(y => new FiscalYearDto(y.Id, y.Year, D(y.StartDate), D(y.EndDate), y.Status, y.VatPeriodicity, y.ClosedAt, y.LockedAt, y.LockReason,
            y.RowVersion, periods[y.Id].Select(p => new AccountingPeriodDto(p.Id, p.PeriodNo, D(p.StartDate), D(p.EndDate), (PeriodState)p.Status, p.IsImported,
                p.SoftClosedAt, p.SoftClosedByName, p.ClosedAt, p.ClosedByName, p.RowVersion)).ToList())).ToList();
    }

    public async Task<PeriodChange> SetStatusAsync(int companyId, int id, PeriodState status, string? reason, byte[] rowVersion, int userId, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleAsync<(int Id, short Year, byte PeriodNo, DateTime StartDate, DateTime EndDate, byte FromStatus, byte Status,
            byte[] RowVersion)>(Command("fin.usp_AccountingPeriod_SetStatus", new
            {
                CompanyId = companyId, Id = id, Status = (byte)status, Reason = reason, RowVersion = rowVersion, UserId = userId,
            }, ct)).ConfigureAwait(false);
        return new PeriodChange(r.Id, r.Year, r.PeriodNo, D(r.StartDate), D(r.EndDate), (PeriodState)r.FromStatus, (PeriodState)r.Status, r.RowVersion);
    }

    public async Task<PeriodState?> StateAsync(int companyId, DateOnly day, CancellationToken ct) =>
        (PeriodState?)await session.Connection.QuerySingleOrDefaultAsync<byte?>(Command("fin.usp_AccountingPeriod_State", new
        {
            CompanyId = companyId, Day = Day(day),
        }, ct)).ConfigureAwait(false);

    public async Task<IReadOnlyList<PeriodHistoryDto>> HistoryAsync(int companyId, int id, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(byte FromStatus, byte ToStatus, string? Reason, DateTime ChangedAt, string? ChangedByName)>(Command(
            "fin.usp_AccountingPeriod_History", new { CompanyId = companyId, Id = id }, ct)).ConfigureAwait(false))
        .Select(h => new PeriodHistoryDto((PeriodState)h.FromStatus, (PeriodState)h.ToStatus, h.Reason, h.ChangedAt, h.ChangedByName)).ToList();

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private static DateOnly D(DateTime value) => DateOnly.FromDateTime(value);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record YearRow(int Id, short Year, DateTime StartDate, DateTime EndDate, byte Status, byte VatPeriodicity, DateTime? ClosedAt, DateTime? LockedAt,
        string? LockReason, byte[] RowVersion);

    private sealed record PeriodRow(int Id, int FiscalYearId, byte PeriodNo, DateTime StartDate, DateTime EndDate, byte Status, bool IsImported,
        DateTime? SoftClosedAt, string? SoftClosedByName, DateTime? ClosedAt, string? ClosedByName, byte[] RowVersion);
}

/// <summary>Jobul de perioade (02.02, C3 3.1): zilnic la 00:20, ora Romaniei, creeaza anul urmator al firmelor cu ani financiari, cu 60 de zile inainte.</summary>
public sealed class FiscalYearCreation(FinanceConnection connection, TimeProvider clock, ILogger<FiscalYearCreation> log)
{
    public const string JobId = "fin-fiscal-years";

    public const string Schedule = "20 0 * * *";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        await using var sql = new SqlConnection(connection.ConnectionString);
        await sql.OpenAsync(ct).ConfigureAwait(false);
        var due = (await sql.QueryAsync<(int CompanyId, short Year, DateTime StartDate, DateTime EndDate, byte VatPeriodicity)>(new CommandDefinition(
            "fin.usp_FiscalYear_DueForCreation", new { Today = BusinessDay.Today(clock).ToDateTime(TimeOnly.MinValue) }, commandType: CommandType.StoredProcedure,
            cancellationToken: ct)).ConfigureAwait(false)).ToList();
        foreach (var y in due)
        {
            await sql.ExecuteAsync(new CommandDefinition("fin.usp_FiscalYear_Create", new
            {
                y.CompanyId, y.Year, y.StartDate, y.EndDate, y.VatPeriodicity, UserId = 1,
            }, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
            log.LogInformation("An financiar {Year} creat pentru firma {CompanyId}.", y.Year, y.CompanyId);
        }

        return due.Count;
    }
}
