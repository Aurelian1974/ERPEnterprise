using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Declarations;

namespace Erp.Finance.Infrastructure.Declarations;

/// <summary>Citirile D406 prin fin.usp_Saft_* si salvarea fisierului in flux prin fin.usp_TaxReturn_SaveFile.</summary>
public sealed class SaftStore(IDbSession session) : ISaftStore
{
    public Task<SaftSummary> SummaryAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SaftSummary>(Command("fin.usp_Saft_Summary", Period(companyId, from, to), ct));

    public async Task<IReadOnlyList<SaftIssueDto>> CheckAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<SaftIssueDto>(Command("fin.usp_Saft_Check", Period(companyId, from, to), ct)).ConfigureAwait(false)).ToList();

    public async Task<SaftMasterData> MasterDataAsync(int companyId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_Saft_ReadMasterData", Period(companyId, from, to), ct)).ConfigureAwait(false);
        var accounts = (await grid.ReadAsync<SaftAccount>().ConfigureAwait(false)).ToList();
        var partners = (await grid.ReadAsync<SaftPartner>().ConfigureAwait(false)).ToList();
        var rates = (await grid.ReadAsync<SaftVatRate>().ConfigureAwait(false)).ToList();
        var journals = (await grid.ReadAsync<SaftJournal>().ConfigureAwait(false)).ToList();
        return new SaftMasterData(accounts, partners, rates, journals);
    }

    public async Task<IReadOnlyList<SaftLedgerLine>> LedgerPageAsync(int companyId, int journalId, DateOnly from, DateOnly to, long afterEntryId, int afterLine,
        int take, CancellationToken ct) =>
        (await session.Connection.QueryAsync<SaftLedgerLine>(Command("fin.usp_Saft_ReadGeneralLedger", new
        {
            CompanyId = companyId, JournalId = journalId, From = D(from), To = D(to), AfterEntryId = afterEntryId, AfterLine = afterLine, Take = take,
        }, ct)).ConfigureAwait(false)).ToList();

    public async Task<IReadOnlyList<SaftInvoiceRow>> InvoicePageAsync(int companyId, byte direction, DateOnly from, DateOnly to, long afterId, int take,
        CancellationToken ct) =>
        (await session.Connection.QueryAsync<SaftInvoiceRow>(Command("fin.usp_Saft_ReadInvoices", new
        {
            CompanyId = companyId, Direction = direction, From = D(from), To = D(to), AfterId = afterId, Take = take,
        }, ct)).ConfigureAwait(false)).ToList();

    public async Task SaveFileAsync(int companyId, int returnId, string fileName, Stream content, int userId, CancellationToken ct)
    {
        await using var command = session.Connection.CreateCommand();
        command.CommandText = "fin.usp_TaxReturn_SaveFile";
        command.CommandType = CommandType.StoredProcedure;
        command.Transaction = session.Transaction;
        command.CommandTimeout = 0;
        Add(command, "@CompanyId", DbType.Int32, companyId);
        Add(command, "@Id", DbType.Int32, returnId);
        Add(command, "@Kind", DbType.Byte, (byte)1);
        Add(command, "@FileName", DbType.String, fileName);
        Add(command, "@Content", DbType.Binary, content, -1);
        Add(command, "@UserId", DbType.Int32, userId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void Add(System.Data.Common.DbCommand command, string name, DbType type, object value, int? size = null)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Value = value;
        if (size is { } s)
        {
            p.Size = s;
        }

        command.Parameters.Add(p);
    }

    private static object Period(int companyId, DateOnly from, DateOnly to) => new { CompanyId = companyId, From = D(from), To = D(to) };

    private static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct, commandTimeout: 0);
}
