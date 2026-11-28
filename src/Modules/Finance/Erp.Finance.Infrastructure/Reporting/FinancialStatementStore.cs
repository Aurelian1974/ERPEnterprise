using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Reporting;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Reporting;

/// <summary>Situatiile financiare prin fin.usp_FinancialStatement_Save, _Get, _Transition, _GetFile.</summary>
public sealed class FinancialStatementStore(IDbSession session) : IFinancialStatementStore
{
    public Task<int> SaveAsync(int companyId, int year, byte kind, byte category, bool isBalanced, IReadOnlyList<StatementLine> lines, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_FinancialStatement_Save", new
        {
            CompanyId = companyId, Year = (short)year, Kind = kind, Category = category, IsBalanced = isBalanced, UserId = userId,
            Lines = JsonSerializer.Serialize(lines.Select(l => new
            {
                formCode = l.FormCode, rowCode = l.RowCode, label = l.Label, sortOrder = l.SortOrder, isTotal = l.IsTotal, currentYear = l.CurrentYear, priorYear = l.PriorYear,
            })),
        }, ct));

    public async Task<FinancialStatementDto?> GetAsync(int companyId, int year, byte kind, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_FinancialStatement_Get", new { CompanyId = companyId, Year = (short)year, Kind = kind }, ct))
            .ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<Header>().ConfigureAwait(false);
        var lines = (await grid.ReadAsync<(string FormCode, string RowCode, string Label, bool IsTotal, decimal? CurrentYear, decimal? PriorYear)>().ConfigureAwait(false))
            .Select(l => new StatementLineDto(l.FormCode, l.RowCode, l.Label, l.IsTotal, Money.Format(l.CurrentYear), Money.Format(l.PriorYear))).ToList();
        return h is null ? null : new FinancialStatementDto(h.Id, h.Year, h.Kind, h.EntityCategory, h.Status, h.IsBalanced, h.CalculatedAt, h.GeneratedAt, h.SubmittedAt,
            h.RegistrationNumber, h.FileName, h.RowVersion, lines);
    }

    public Task<byte[]> TransitionAsync(int companyId, int year, byte kind, byte[] rowVersion, string action, string? fileName, byte[]? content,
        string? registrationNumber, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]>(Command("fin.usp_FinancialStatement_Transition", new
        {
            CompanyId = companyId, Year = (short)year, Kind = kind, RowVersion = rowVersion, Action = action, FileName = fileName, FileContent = content,
            RegistrationNumber = registrationNumber, UserId = userId,
        }, ct));

    public async Task<(string FileName, byte[] Content)?> GetFileAsync(int companyId, int year, byte kind, CancellationToken ct) =>
        await session.Connection.QuerySingleOrDefaultAsync<(string FileName, byte[] Content)?>(Command("fin.usp_FinancialStatement_GetFile",
            new { CompanyId = companyId, Year = (short)year, Kind = kind }, ct)).ConfigureAwait(false);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record Header(int Id, short Year, byte Kind, byte EntityCategory, byte Status, bool IsBalanced, DateTime CalculatedAt, DateTime? GeneratedAt,
        DateTime? SubmittedAt, string? RegistrationNumber, string? FileName, byte[] RowVersion);
}
