using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Reporting;
using Erp.Finance.Contracts.Reporting;
using Erp.Finance.Domain.Reporting;

namespace Erp.Finance.Infrastructure.Reporting;

/// <summary>Soldurile si miscarile prin fin.usp_Balance_Get, fin.usp_AccountLedger_Get, fin.usp_GeneralLedger_Get (NET-06).</summary>
public sealed class BalanceStore(IDbSession session) : IBalanceStore
{
    public async Task<BalanceData> BalanceAsync(BalanceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_Balance_Get", new
        {
            request.CompanyId, request.ChartId, From = Day(request.From), To = Day(request.To), IncludeClosing = request.IncludeClosingEntries,
            request.AccountFilter, request.ByPartner, request.ByCostCenter, request.ByProject,
        }, ct)).ConfigureAwait(false);
        var year = await grid.ReadSingleAsync<(DateTime Start, DateTime End, bool IsAlternative)>().ConfigureAwait(false);
        var inputs = (await grid.ReadAsync<BalanceInput>().ConfigureAwait(false)).ToList();
        var accounts = (await grid.ReadAsync<BalanceAccount>().ConfigureAwait(false)).ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        return new BalanceData(DateOnly.FromDateTime(year.Start), DateOnly.FromDateTime(year.End), year.IsAlternative, inputs, accounts);
    }

    public async Task<(LedgerHeader Header, IReadOnlyList<LedgerLineRecord> Lines)> LedgerAsync(int companyId, LedgerFilter filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_AccountLedger_Get", new
        {
            CompanyId = companyId, filter.AccountCode, From = Day(filter.From), To = Day(filter.To), filter.PartnerId, filter.CostCenterId, filter.ProjectId,
            filter.IncludeClosing, filter.Skip, filter.Take,
        }, ct)).ConfigureAwait(false);
        var header = await grid.ReadSingleAsync<LedgerHeader>().ConfigureAwait(false);
        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).Select(l => new LedgerLineRecord(l.LineId, l.EntryId, l.JournalCode, l.Number,
            DateOnly.FromDateTime(l.AccountingDate), l.AccountCode, l.SourceKind, l.SourceDocumentType, l.SourceDocumentNumber, l.Description, l.PartnerName,
            l.CostCenterCode, l.ProjectCode, l.Debit, l.Credit, l.Balance)).ToList();
        return (header, lines);
    }

    public async Task<IReadOnlyList<(string AccountCode, string CounterAccount, decimal Debit, decimal Credit)>> GeneralLedgerAsync(int companyId, DateOnly from,
        DateOnly to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(string, string, decimal, decimal)>(Command("fin.usp_GeneralLedger_Get", new
        {
            CompanyId = companyId, From = Day(from), To = Day(to),
        }, ct)).ConfigureAwait(false)).ToList();

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record LineRow(long LineId, long EntryId, string JournalCode, int? Number, DateTime AccountingDate, string AccountCode, byte SourceKind,
        string? SourceDocumentType, string? SourceDocumentNumber, string? Description, string? PartnerName, string? CostCenterCode, string? ProjectCode,
        decimal Debit, decimal Credit, decimal Balance);
}
