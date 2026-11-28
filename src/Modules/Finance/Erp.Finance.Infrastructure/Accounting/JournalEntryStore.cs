using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Accounting;
using Erp.Finance.Contracts.Accounting;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Accounting;

/// <summary>Notele contabile prin fin.usp_JournalEntry_*, fin.usp_Journal_List (NET-06).</summary>
public sealed class JournalEntryStore(IDbSession session) : IJournalEntryStore
{
    public async Task<IReadOnlyList<JournalDto>> JournalsAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<JournalDto>(Command("fin.usp_Journal_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false)).ToList();

    public async Task<EntrySaved> SaveDraftAsync(long? id, int companyId, int journalId, DateOnly entryDate, DateOnly accountingDate, EntrySourceKind kind,
        string? description, string? documentRef, IReadOnlyList<JournalLineRecord> lines, byte[]? rowVersion, int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<(long Id, byte[] RowVersion)>(Command("fin.usp_JournalEntry_SaveDraft", new
        {
            Id = id, CompanyId = companyId, JournalId = journalId, EntryDate = Day(entryDate), AccountingDate = Day(accountingDate), SourceKind = (byte)kind,
            Description = description, DocumentRef = documentRef, RowVersion = rowVersion, UserId = userId,
            Lines = JsonSerializer.Serialize(lines.Select(l => new
            {
                accountId = l.AccountId, side = l.Side, amountRon = l.AmountRon, currencyCode = l.CurrencyCode, amountCurrency = l.AmountCurrency,
                partnerId = l.PartnerId, employeeId = l.EmployeeId, costCenterId = l.CostCenterId, projectId = l.ProjectId, warehouseId = l.WarehouseId,
                siteId = l.SiteId, description = l.Description,
            })),
        }, ct)).ConfigureAwait(false);
        return new EntrySaved(row.Id, row.RowVersion);
    }

    public async Task<JournalEntryHeader> ValidateAsync(int companyId, long id, byte[] rowVersion, bool fourEyes, int userId, CancellationToken ct) =>
        Header(await session.Connection.QuerySingleAsync<HeaderRow>(Command("fin.usp_JournalEntry_Validate", new
        {
            CompanyId = companyId, Id = id, RowVersion = rowVersion, FourEyes = fourEyes, UserId = userId,
        }, ct)).ConfigureAwait(false));

    public async Task<JournalEntryHeader> ReverseAsync(int companyId, long id, DateOnly accountingDate, string description, bool black, bool allowAutomatic,
        int userId, CancellationToken ct) =>
        Header(await session.Connection.QuerySingleAsync<HeaderRow>(Command("fin.usp_JournalEntry_Reverse", new
        {
            CompanyId = companyId, Id = id, AccountingDate = Day(accountingDate), Description = description, Black = black, AllowAutomatic = allowAutomatic,
            UserId = userId,
        }, ct)).ConfigureAwait(false));

    public Task DeleteDraftAsync(int companyId, long id, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_JournalEntry_DeleteDraft", new { CompanyId = companyId, Id = id }, ct));

    public async Task<JournalEntryDto?> GetAsync(int companyId, long id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_JournalEntry_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var e = (await grid.ReadAsync<EntryRow>().ConfigureAwait(false)).SingleOrDefault();
        if (e is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).Select(l => new JournalLineDto(l.Id, l.LineNo, l.AccountId, l.AccountCode, l.AccountName,
            l.Side, Money.Format(l.AmountRon), l.CurrencyCode?.Trim(), l.AmountCurrency is { } c ? Money.Format(c) : null, l.PartnerId, l.PartnerName, l.EmployeeId,
            l.CostCenterId, l.CostCenterCode, l.ProjectId, l.ProjectCode, l.WarehouseId, l.WarehouseCode, l.SiteId, l.SiteCode, l.Description, l.ResolvedFrom))
            .ToList();
        return new JournalEntryDto(e.Id, e.JournalId, e.JournalCode, e.Number, JournalEntryNumbers.Display(e.JournalCode, e.Number), D(e.EntryDate),
            D(e.AccountingDate), (EntrySourceKind)e.SourceKind, e.SourceModule, e.SourceDocumentType, e.SourceDocumentId, e.SourceDocumentNumber, e.Status,
            e.ReversalOfId, e.ReversedById, e.Description, Money.Format(e.TotalDebit), Money.Format(e.TotalCredit), e.ValidatedAt, e.ValidatedByName,
            e.CreatedByName, e.RowVersion, lines);
    }

    public async Task<JournalEntryPageDto> SearchAsync(int companyId, JournalSearch search, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_JournalEntry_Search", new
        {
            CompanyId = companyId, From = Day(search.From), To = Day(search.To), search.JournalId, SourceKind = (byte?)search.SourceKind, search.Status,
            search.AccountCode, search.Search, search.Skip, search.Take,
        }, ct)).ConfigureAwait(false);
        var total = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        var items = (await grid.ReadAsync<ItemRow>().ConfigureAwait(false)).Select(i => new JournalEntryItemDto(i.Id, i.JournalCode, i.Number,
            JournalEntryNumbers.Display(i.JournalCode, i.Number), D(i.AccountingDate), (EntrySourceKind)i.SourceKind, i.SourceDocumentType, i.SourceDocumentNumber,
            i.Status, i.Description, Money.Format(i.TotalDebit), i.LineCount, i.DebitAccounts, i.CreditAccounts)).ToList();
        return new JournalEntryPageDto(total, items);
    }

    public Task<bool> TouchesControlledAccountsAsync(long id, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<bool>(Command("fin.usp_JournalEntry_ControlledAccounts", new { Id = id }, ct));

    public Task<string?> SettingAsync(int companyId, string code, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<string?>(Command("fin.usp_CompanySetting_Get", new { CompanyId = companyId, Code = code }, ct));

    private static JournalEntryHeader Header(HeaderRow h) =>
        new(h.Id, h.CompanyId, h.JournalCode, h.Number, D(h.AccountingDate), (EntrySourceKind)h.SourceKind, h.SourceDocumentType, h.SourceDocumentId, h.TotalDebit,
            h.RowVersion);

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private static DateOnly D(DateTime value) => DateOnly.FromDateTime(value);

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record HeaderRow(long Id, int CompanyId, string JournalCode, int Number, DateTime AccountingDate, byte SourceKind, string? SourceDocumentType,
        long? SourceDocumentId, decimal TotalDebit, byte[] RowVersion);

    private sealed record EntryRow(long Id, int JournalId, string JournalCode, int? Number, DateTime EntryDate, DateTime AccountingDate, byte SourceKind,
        string? SourceModule, string? SourceDocumentType, long? SourceDocumentId, string? SourceDocumentNumber, byte Status, long? ReversalOfId, long? ReversedById,
        string? Description, decimal TotalDebit, decimal TotalCredit, DateTime? ValidatedAt, string? ValidatedByName, string? CreatedByName, int CreatedBy,
        byte[] RowVersion);

    private sealed record LineRow(long Id, int LineNo, int AccountId, string AccountCode, string AccountName, byte Side, decimal AmountRon, string? CurrencyCode,
        decimal? AmountCurrency, int? PartnerId, string? PartnerName, int? EmployeeId, int? CostCenterId, string? CostCenterCode, int? ProjectId,
        string? ProjectCode, int? WarehouseId, string? WarehouseCode, int? SiteId, string? SiteCode, string? Description, byte? ResolvedFrom);

    private sealed record ItemRow(long Id, string JournalCode, int? Number, DateTime AccountingDate, byte SourceKind, string? SourceDocumentType,
        string? SourceDocumentNumber, byte Status, string? Description, decimal TotalDebit, int LineCount, string? DebitAccounts, string? CreditAccounts);
}
