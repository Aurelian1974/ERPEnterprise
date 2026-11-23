using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Bank;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;

namespace Erp.Finance.Infrastructure.Bank;

/// <summary>Extrasele bancare prin fin.usp_BankStatement_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class BankStatementStore(IDbSession session) : IBankStatementStore
{
    public Task<BankImportAccount?> GetImportAccountAsync(int companyId, int treasuryAccountId, CancellationToken ct) =>
        GetAccountAsync(new { CompanyId = companyId, TreasuryAccountId = treasuryAccountId }, ct);

    public Task<BankImportAccount?> FindImportAccountByIbanAsync(int companyId, string iban, CancellationToken ct) =>
        GetAccountAsync(new { CompanyId = companyId, Iban = iban }, ct);

    private async Task<BankImportAccount?> GetAccountAsync(object parameters, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<AccountRow>(Command("fin.usp_BankImportAccount_Get", parameters, ct)).ConfigureAwait(false);
        return row is null
            ? null
            : new BankImportAccount(row.Id, row.Code, row.CurrencyCode.Trim(), row.Iban, row.StatementFormat is { } f ? (StatementFormat)f : null, row.CsvMapping);
    }

    public Task<decimal?> GetPreviousClosingAsync(int treasuryAccountId, DateOnly before, CancellationToken ct) =>
        session.Connection.QuerySingleOrDefaultAsync<decimal?>(Command("fin.usp_BankStatement_PreviousClosing",
            new { TreasuryAccountId = treasuryAccountId, Before = before.ToDateTime(TimeOnly.MinValue) }, ct));

    public Task<int> AddFileAsync(int companyId, int treasuryAccountId, string fileName, string sha256, byte[] content, int userId, CancellationToken ct) =>
        session.Connection.ExecuteScalarAsync<int>(Command("fin.usp_BankStatementFile_Add", new
        {
            CompanyId = companyId, TreasuryAccountId = treasuryAccountId, FileName = fileName, FileSha256 = sha256, Content = content, UserId = userId,
        }, ct));

    public async Task<BankStatementImported> ImportAsync(int companyId, BankStatementToImport statement, int userId, CancellationToken ct)
    {
        var lines = new DataTable();
        lines.Columns.Add("LineNumber", typeof(int));
        lines.Columns.Add("BookingDate", typeof(DateTime));
        lines.Columns.Add("ValueDate", typeof(DateTime));
        lines.Columns.Add("Amount", typeof(decimal));
        lines.Columns.Add("CounterpartyName", typeof(string));
        lines.Columns.Add("CounterpartyIban", typeof(string));
        lines.Columns.Add("CounterpartyBic", typeof(string));
        lines.Columns.Add("RemittanceInfo", typeof(string));
        lines.Columns.Add("EndToEndId", typeof(string));
        lines.Columns.Add("BankReference", typeof(string));
        lines.Columns.Add("BankTransactionCode", typeof(string));
        lines.Columns.Add("ForeignAmount", typeof(decimal));
        lines.Columns.Add("ForeignCurrency", typeof(string));
        lines.Columns.Add("ExtractedFiscalCode", typeof(string));
        lines.Columns.Add("ExtractedDocumentNo", typeof(string));
        for (var i = 0; i < statement.Lines.Count; i++)
        {
            var l = statement.Lines[i];
            lines.Rows.Add(i + 1, l.BookingDate.ToDateTime(TimeOnly.MinValue), l.ValueDate is { } v ? v.ToDateTime(TimeOnly.MinValue) : DBNull.Value, l.Amount,
                (object?)l.CounterpartyName ?? DBNull.Value, (object?)l.CounterpartyIban ?? DBNull.Value, (object?)l.CounterpartyBic ?? DBNull.Value,
                (object?)l.RemittanceInfo ?? DBNull.Value, (object?)l.EndToEndId ?? DBNull.Value, statement.References[i],
                (object?)l.BankTransactionCode ?? DBNull.Value, (object?)l.ForeignAmount ?? DBNull.Value, (object?)l.ForeignCurrency?.ToUpperInvariant() ?? DBNull.Value,
                (object?)RemittanceRules.ExtractFiscalCode(l.RemittanceInfo) ?? DBNull.Value, (object?)RemittanceRules.ExtractDocumentNo(l.RemittanceInfo) ?? DBNull.Value);
        }

        var row = await session.Connection.QuerySingleAsync<ImportedRow>(Command("fin.usp_BankStatement_Import", new
        {
            CompanyId = companyId, statement.TreasuryAccountId, statement.BankStatementFileId, statement.StatementNo,
            StatementDate = statement.StatementDate.ToDateTime(TimeOnly.MinValue), PeriodFrom = statement.PeriodFrom.ToDateTime(TimeOnly.MinValue),
            PeriodTo = statement.PeriodTo.ToDateTime(TimeOnly.MinValue), statement.CurrencyCode, statement.OpeningBalance, statement.ClosingBalance,
            Source = (byte)statement.Source, Lines = lines.AsTableValuedParameter("fin.BankStatementLineList"), UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new BankStatementImported(row.BankStatementId, row.LineCount, row.SkippedCount, (ContinuityStatus)row.ContinuityStatus);
    }

    public Task<byte[]> AcceptGapAsync(int companyId, int bankStatementId, string reason, byte[] rowVersion, int userId, CancellationToken ct) =>
        session.Connection.ExecuteScalarAsync<byte[]>(Command("fin.usp_BankStatement_AcceptGap", new
        {
            CompanyId = companyId, BankStatementId = bankStatementId, Reason = reason, RowVersion = rowVersion, UserId = userId,
        }, ct))!;

    public async Task<IReadOnlyList<BankStatementRow>> ListAsync(int companyId, int? treasuryAccountId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        (await session.Connection.QueryAsync<StatementRow>(Command("fin.usp_BankStatement_List", new
        {
            CompanyId = companyId, TreasuryAccountId = treasuryAccountId, From = from?.ToDateTime(TimeOnly.MinValue), To = to?.ToDateTime(TimeOnly.MinValue),
        }, ct)).ConfigureAwait(false)).Select(Map).ToList();

    public async Task<BankStatementDetailRow?> GetAsync(int companyId, int bankStatementId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_BankStatement_Get",
            new { CompanyId = companyId, BankStatementId = bankStatementId }, ct)).ConfigureAwait(false);
        if (grid.IsConsumed)
        {
            return null;
        }

        var head = await grid.ReadSingleAsync<DetailRow>().ConfigureAwait(false);
        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false)).Select(l => new BankStatementLineRow(l.Id, l.LineNumber,
            DateOnly.FromDateTime(l.BookingDate), l.ValueDate is { } v ? DateOnly.FromDateTime(v) : null, l.Amount, l.CounterpartyName, l.CounterpartyIban,
            l.CounterpartyBic, l.RemittanceInfo, l.EndToEndId, l.BankReference, l.BankTransactionCode, l.ForeignAmount, l.ForeignCurrency?.Trim(),
            (BankLineMatchStatus)l.MatchStatus, l.PartnerId, l.PartnerName, l.RowVersion)).ToList();
        var statement = Map(new StatementRow(head.Id, head.TreasuryAccountId, head.AccountCode, head.AccountName, head.StatementNo, head.StatementDate,
            head.PeriodFrom, head.PeriodTo, head.CurrencyCode, head.OpeningBalance, head.ClosingBalance, head.Source, head.Status, head.ContinuityStatus,
            head.LineCount, head.ValidatedCount, head.RowVersion));
        return new BankStatementDetailRow(statement, head.FileName, head.GapReason, head.GapAcceptedAt, head.GapAcceptedByName, head.PreviousClosingBalance,
            lines);
    }

    public async Task<BankReconciliationDto?> GetReconciliationAsync(int companyId, int treasuryAccountId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_BankReconciliation_Get",
            new { CompanyId = companyId, TreasuryAccountId = treasuryAccountId }, ct)).ConfigureAwait(false);
        if (grid.IsConsumed)
        {
            return null;
        }

        var h = await grid.ReadSingleAsync<ReconciliationRow>().ConfigureAwait(false);
        var lines = (await grid.ReadAsync<ReconciliationLineRow>().ConfigureAwait(false)).Select(l => new ReconciliationLineDto(l.Id, l.BankStatementId,
            l.StatementNo, l.LineNumber, DateOnly.FromDateTime(l.BookingDate), Money.Format(l.Amount), l.CounterpartyName, l.CounterpartyIban, l.RemittanceInfo,
            (BankLineMatchStatus)l.MatchStatus, l.PartnerId, l.PartnerName, (ContinuityStatus)l.ContinuityStatus, l.SuggestionCount, l.RowVersion)).ToList();
        var orders = (await grid.ReadAsync<OrderRow>().ConfigureAwait(false)).Select(o => new UnconfirmedOrderDto(o.Id, o.Number,
            DateOnly.FromDateTime(o.DocumentDate), o.BeneficiaryName, Money.Format(-o.Amount), o.ApprovedAt is not null, o.PaymentBatchId, o.BatchNumber,
            o.BatchStatus)).ToList();
        var types = (await grid.ReadAsync<BankOperationTypeDto>().ConfigureAwait(false)).ToList();
        var accounts = (await grid.ReadAsync<BankAccountOptionDto>().ConfigureAwait(false)).ToList();
        return new BankReconciliationDto(h.TreasuryAccountId, h.AccountCode, h.AccountName, h.CurrencyCode.Trim(), h.BankStatementId, h.StatementNo,
            h.StatementDate is { } d ? DateOnly.FromDateTime(d) : null, h.ClosingBalance is { } c ? Money.Format(c) : null, Money.Format(-h.UnconfirmedAmount),
            h.ClosingBalance is { } b ? Money.Format(b - h.UnconfirmedAmount) : null, h.OpenLineCount, lines, orders, types, accounts);
    }

    public async Task<IReadOnlyList<BankAccountLookupDto>> ListAccountsAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int Id, string Code, string Name, byte Kind, string CurrencyCode, int? BankFormatProfileId, string? ImportFolder)>(
            Command("fin.usp_BankAccount_Options", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
        .Select(a => new BankAccountLookupDto(a.Id, a.Code, a.Name, a.Kind, a.CurrencyCode.Trim())
        {
            BankFormatProfileId = a.BankFormatProfileId, ImportFolder = a.ImportFolder,
        }).ToList();

    public async Task<IReadOnlyList<BankOperationTypeDto>> ListOperationTypesAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<BankOperationTypeDto>(Command("fin.usp_BankOperationType_List", new { }, ct)).ConfigureAwait(false)).ToList();

    private sealed record ReconciliationRow(int TreasuryAccountId, string AccountCode, string AccountName, string CurrencyCode, int? BankStatementId,
        string? StatementNo, DateTime? StatementDate, decimal? ClosingBalance, decimal UnconfirmedAmount, int OpenLineCount);

    private sealed record ReconciliationLineRow(long Id, int BankStatementId, string? StatementNo, int LineNumber, DateTime BookingDate, decimal Amount,
        string? CounterpartyName, string? CounterpartyIban, string? RemittanceInfo, byte MatchStatus, int? PartnerId, string? PartnerName, byte ContinuityStatus,
        int SuggestionCount, byte[] RowVersion);

    private sealed record OrderRow(int Id, string? Number, DateTime DocumentDate, string BeneficiaryName, decimal Amount, DateTime? ApprovedAt,
        int? PaymentBatchId, string? BatchNumber, byte? BatchStatus);

    private static BankStatementRow Map(StatementRow r) => new(r.Id, r.TreasuryAccountId, r.AccountCode, r.AccountName, r.StatementNo,
        DateOnly.FromDateTime(r.StatementDate), DateOnly.FromDateTime(r.PeriodFrom), DateOnly.FromDateTime(r.PeriodTo), r.CurrencyCode.Trim(), r.OpeningBalance,
        r.ClosingBalance, (StatementSource)r.Source, (BankStatementStatus)r.Status, (ContinuityStatus)r.ContinuityStatus, r.LineCount, r.ValidatedCount,
        r.RowVersion);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record AccountRow(int Id, string Code, string CurrencyCode, string? Iban, byte? StatementFormat, string? CsvMapping);

    private sealed record ImportedRow(int BankStatementId, int LineCount, int SkippedCount, byte ContinuityStatus);

    private sealed record StatementRow(int Id, int TreasuryAccountId, string AccountCode, string AccountName, string? StatementNo, DateTime StatementDate,
        DateTime PeriodFrom, DateTime PeriodTo, string CurrencyCode, decimal OpeningBalance, decimal ClosingBalance, byte Source, byte Status,
        byte ContinuityStatus, int LineCount, int ValidatedCount, byte[] RowVersion);

    private sealed record DetailRow(int Id, int TreasuryAccountId, string AccountCode, string AccountName, string? StatementNo, DateTime StatementDate,
        DateTime PeriodFrom, DateTime PeriodTo, string CurrencyCode, decimal OpeningBalance, decimal ClosingBalance, byte Source, byte Status,
        byte ContinuityStatus, int LineCount, int ValidatedCount, byte[] RowVersion, string? FileName, string? GapReason, DateTime? GapAcceptedAt,
        string? GapAcceptedByName, decimal? PreviousClosingBalance);

    private sealed record LineRow(long Id, int LineNumber, DateTime BookingDate, DateTime? ValueDate, decimal Amount, string? CounterpartyName,
        string? CounterpartyIban, string? CounterpartyBic, string? RemittanceInfo, string? EndToEndId, string BankReference, string? BankTransactionCode,
        decimal? ForeignAmount, string? ForeignCurrency, byte MatchStatus, int? PartnerId, string? PartnerName, byte[] RowVersion);
}

/// <summary>Profilele de format bancar prin fin.usp_BankFormatProfile_* si fin.usp_TreasuryAccount_SetBankFormat.</summary>
public sealed class BankFormatProfileStore(IDbSession session) : IBankFormatProfileStore
{
    public async Task<IReadOnlyList<BankFormatProfileDto>> ListAsync(int companyId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ProfileRow>(new CommandDefinition("fin.usp_BankFormatProfile_List", new { CompanyId = companyId },
            session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false))
        .Select(p => new BankFormatProfileDto(p.Id, p.Code, p.Name, p.BankBic, (StatementFormat)p.StatementFormat, p.CsvMapping,
            (PaymentFileFormat)p.PaymentFormat, p.PaymentMapping, p.MaxRemittanceLength, p.IsSystem, p.IsActive, p.AccountCount, p.RowVersion)).ToList();

    public async Task<FinanceSaved> SaveAsync(SaveBankFormatProfile profile, int userId, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(new CommandDefinition("fin.usp_BankFormatProfile_Save", new
        {
            profile.Id, profile.Code, profile.Name, profile.BankBic, StatementFormat = (byte)profile.StatementFormat, profile.CsvMapping,
            PaymentFormat = (byte)profile.PaymentFormat, profile.PaymentMapping, profile.MaxRemittanceLength, profile.IsActive, profile.RowVersion,
            UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        return new FinanceSaved(row.Id, row.RowVersion);
    }

    public Task SetAccountFormatAsync(int companyId, int treasuryAccountId, int? bankFormatProfileId, string? importFolder, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(new CommandDefinition("fin.usp_TreasuryAccount_SetBankFormat", new
        {
            CompanyId = companyId, TreasuryAccountId = treasuryAccountId, BankFormatProfileId = bankFormatProfileId, ImportFolder = importFolder,
            UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));

    private sealed record ProfileRow(int Id, string Code, string Name, string? BankBic, byte StatementFormat, string? CsvMapping, byte PaymentFormat,
        string? PaymentMapping, short MaxRemittanceLength, bool IsSystem, bool IsActive, int AccountCount, byte[] RowVersion);
}
