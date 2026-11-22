using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Treasury;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Treasury;

namespace Erp.Finance.Infrastructure.Treasury;

/// <summary>Conturile de trezorerie prin procedurile fin.usp_TreasuryAccount_* (NET-06).</summary>
public sealed class TreasuryAccountStore(IDbSession session) : ITreasuryAccountStore
{
    public async Task<IReadOnlyList<TreasuryAccountItemDto>> ListAsync(int companyId, string? search, bool includeInactive, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ItemRow>(new CommandDefinition("fin.usp_TreasuryAccount_List",
            new { CompanyId = companyId, Search = search, IncludeInactive = includeInactive }, session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false))
        .Select(r => new TreasuryAccountItemDto(r.Id, (TreasuryAccountKind)r.Kind, r.Code, r.Name, r.BranchId, r.BranchName, r.CurrencyCode.Trim(),
            r.GlAccountCode, r.CompanyBankAccountId, r.BankName, r.Iban, r.IsActive, r.CashierCount))
        .ToList();

    public async Task<TreasuryLookupsDto> GetLookupsAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("fin.usp_TreasuryAccount_Lookups",
            new { CompanyId = companyId }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var branches = (await grid.ReadAsync<TreasuryOptionDto>().ConfigureAwait(false)).ToList();
        var banks = (await grid.ReadAsync<BankRow>().ConfigureAwait(false)).Select(b => new TreasuryBankAccountOptionDto(b.Id, b.Iban, b.BankName,
            b.CurrencyCode.Trim())).ToList();
        var currencies = (await grid.ReadAsync<string>().ConfigureAwait(false)).Select(c => c.Trim()).ToList();
        var users = (await grid.ReadAsync<TreasuryOptionDto>().ConfigureAwait(false)).ToList();
        var series = (await grid.ReadAsync<TreasurySeriesOptionDto>().ConfigureAwait(false)).ToList();
        return new TreasuryLookupsDto(branches, banks, currencies, users, series);
    }

    public async Task<TreasuryAccountDetailDto?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("fin.usp_TreasuryAccount_Get",
            new { CompanyId = companyId, Id = id }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct))
            .ConfigureAwait(false);
        var row = await grid.ReadSingleOrDefaultAsync<DetailRow>().ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var cashiers = (await grid.ReadAsync<CashierRow>().ConfigureAwait(false))
            .Select(c => new TreasuryCashierDto(c.UserId, c.DisplayName, c.CanValidate, Date(c.ValidFrom), Date(c.ValidTo))).ToList();
        return new TreasuryAccountDetailDto(row.Id, row.CompanyId, (TreasuryAccountKind)row.Kind, row.Code, row.Name, row.BranchId,
            row.CurrencyCode.Trim(), row.CompanyBankAccountId, row.GlAccountCode, row.ResponsibleUserId, row.ReceiptSeriesId, row.CashInSeriesId,
            row.CashOutSeriesId, Money.Format(row.MaxClosingBalance), (CashCloseMode)row.CloseMode, Date(row.OpeningDate), row.IsActive, row.HasDocuments,
            row.CreatedAt, row.ModifiedAt, row.ModifiedByName, cashiers, row.RowVersion);
    }

    public Task<FinanceSaved> SaveAsync(TreasuryAccountRecord account, int userId, CancellationToken ct)
    {
        var cashiers = new DataTable();
        cashiers.Columns.Add("UserId", typeof(int));
        cashiers.Columns.Add("CanValidate", typeof(bool));
        cashiers.Columns.Add("ValidFrom", typeof(DateTime));
        cashiers.Columns.Add("ValidTo", typeof(DateTime));
        foreach (var c in account.Cashiers ?? [])
        {
            cashiers.Rows.Add(c.UserId, c.CanValidate, (object?)DateTime(c.ValidFrom) ?? DBNull.Value, (object?)DateTime(c.ValidTo) ?? DBNull.Value);
        }

        return session.Connection.QuerySingleAsync<FinanceSaved>(new CommandDefinition("fin.usp_TreasuryAccount_Save", new
        {
            account.Id, account.CompanyId, Kind = (byte)account.Kind, account.Code, account.Name, account.BranchId,
            CurrencyCode = new DbString { Value = account.CurrencyCode, Length = 3, IsAnsi = true, IsFixedLength = true },
            account.CompanyBankAccountId, account.GlAccountCode, account.ResponsibleUserId, account.ReceiptSeriesId, account.CashInSeriesId,
            account.CashOutSeriesId, account.MaxClosingBalance, CloseMode = (byte)account.CloseMode, OpeningDate = DateTime(account.OpeningDate),
            account.IsActive, Cashiers = cashiers.AsTableValuedParameter("fin.TreasuryCashierList"), account.RowVersion, UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    internal static DateOnly? Date(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    internal static DateTime? DateTime(DateOnly? value) => value?.ToDateTime(TimeOnly.MinValue);

    private sealed record ItemRow(int Id, byte Kind, string Code, string Name, int? BranchId, string? BranchName, string CurrencyCode,
        string? GlAccountCode, int? CompanyBankAccountId, string? BankName, string? Iban, bool IsActive, int CashierCount);

    private sealed record BankRow(int Id, string Iban, string BankName, string CurrencyCode);

    private sealed record DetailRow(int Id, int CompanyId, byte Kind, string Code, string Name, int? BranchId, string CurrencyCode,
        int? CompanyBankAccountId, string? GlAccountCode, int? ResponsibleUserId, int? ReceiptSeriesId, int? CashInSeriesId, int? CashOutSeriesId,
        decimal? MaxClosingBalance, byte CloseMode, DateTime? OpeningDate, bool IsActive, bool HasDocuments, DateTime CreatedAt, DateTime? ModifiedAt,
        string? ModifiedByName, byte[] RowVersion);

    private sealed record CashierRow(int UserId, string DisplayName, bool CanValidate, DateTime? ValidFrom, DateTime? ValidTo);
}

/// <summary>ITreasuryAccountQueries (F1 6): conturile active ale unei firme si soldul unui cont, pentru celelalte module.</summary>
public sealed class TreasuryAccountQueries(IDbSession session, IExchangeRateProvider rates, IPrecisionService precision) : ITreasuryAccountQueries
{
    public async Task<TreasuryBalanceDto> GetBalanceAsync(int treasuryAccountId, DateOnly day, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<BalanceRow>(new CommandDefinition("fin.usp_TreasuryAccount_GetBalance",
            new { TreasuryAccountId = treasuryAccountId, Day = TreasuryAccountStore.DateTime(day) }, session.Transaction, commandType: CommandType.StoredProcedure,
            cancellationToken: ct)).ConfigureAwait(false)
            ?? throw ErpException.NotFound(FinanceErrorCodes.TreasuryAccountNotFound, "Contul de trezorerie nu există.");
        var currency = row.CurrencyCode.Trim();
        var rate = await rates.GetRateAsync(currency, day, ct).ConfigureAwait(false);
        var policy = await precision.ResolveAsync(row.CompanyId, null, null, ct).ConfigureAwait(false);
        return new TreasuryBalanceDto(row.TreasuryAccountId, day, currency, row.Balance, precision.RoundAmount(row.Balance * rate.PerUnit, policy),
            row.IsDayClosed);
    }

    private sealed record BalanceRow(int TreasuryAccountId, int CompanyId, string CurrencyCode, decimal Balance, bool IsDayClosed);

    public async Task<IReadOnlyList<TreasuryAccountDto>> GetByCompanyAsync(int companyId, TreasuryAccountKind? kind, CancellationToken ct) =>
        (await new TreasuryAccountStore(session).ListAsync(companyId, null, includeInactive: false, ct).ConfigureAwait(false))
        .Where(a => kind is null || a.Kind == kind)
        .Select(a => new TreasuryAccountDto(a.Id, companyId, a.BranchId, a.Code, a.Name, a.Kind, a.CurrencyCode, a.CompanyBankAccountId, a.GlAccountCode, a.IsActive))
        .ToList();
}
