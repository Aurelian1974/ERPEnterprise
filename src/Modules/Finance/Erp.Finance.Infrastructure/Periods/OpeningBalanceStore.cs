using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Periods;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Periods;

/// <summary>Soldurile initiale prin fin.usp_OpeningBalanceImport_* (NET-06).</summary>
public sealed class OpeningBalanceStore(IDbSession session) : IOpeningBalanceStore
{
    public async Task<int> SaveAsync(int companyId, DateOnly startDate, string? fileName, IReadOnlyList<OpeningBalanceRowInput> rows, int userId, CancellationToken ct) =>
        await session.Connection.QuerySingleAsync<int>(Command("fin.usp_OpeningBalanceImport_Save", new
        {
            CompanyId = companyId, StartDate = startDate.ToDateTime(TimeOnly.MinValue), FileName = fileName, UserId = userId,
            Rows = JsonSerializer.Serialize(rows.Select(r => new
            {
                rowNumber = r.RowNumber, accountCode = r.AccountCode.Trim(), partnerId = r.PartnerId, currencyCode = string.IsNullOrWhiteSpace(r.CurrencyCode) ? null : r.CurrencyCode.Trim().ToUpperInvariant(),
                amountCurrency = Money.ParseOptional(r.AmountCurrency), openingDebit = Money.ParseOptional(r.OpeningDebit) ?? 0, openingCredit = Money.ParseOptional(r.OpeningCredit) ?? 0,
                turnoverDebit = Money.ParseOptional(r.TurnoverDebit) ?? 0, turnoverCredit = Money.ParseOptional(r.TurnoverCredit) ?? 0,
                closingDebit = Money.ParseOptional(r.ClosingDebit) ?? 0, closingCredit = Money.ParseOptional(r.ClosingCredit) ?? 0,
            })),
        }, ct)).ConfigureAwait(false);

    public Task ValidateAsync(int companyId, int id, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_OpeningBalanceImport_Validate", new { CompanyId = companyId, Id = id, UserId = userId }, ct));

    public Task CancelAsync(int companyId, int id, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_OpeningBalanceImport_Cancel", new { CompanyId = companyId, Id = id, UserId = userId }, ct));

    public async Task<OpeningBalanceImportDto?> GetAsync(int companyId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_OpeningBalanceImport_Get", new { CompanyId = companyId }, ct)).ConfigureAwait(false);
        var h = (await grid.ReadAsync<(int Id, DateTime StartDate, byte Status, string? FileName, long? OpeningEntryId, long? TurnoverEntryId, DateTime? ValidatedAt,
            byte[] RowVersion)>().ConfigureAwait(false)).ToList();
        var rows = (await grid.ReadAsync<(int RowNumber, string AccountCode, string AccountName, int? PartnerId, string? PartnerName, string? CurrencyCode,
            decimal OpeningDebit, decimal OpeningCredit, decimal TurnoverDebit, decimal TurnoverCredit, decimal ClosingDebit, decimal ClosingCredit)>().ConfigureAwait(false))
            .Select(r => new OpeningBalanceRowDto(r.RowNumber, r.AccountCode, r.AccountName, r.PartnerId, r.PartnerName, r.CurrencyCode?.Trim(), Money.Format(r.OpeningDebit),
                Money.Format(r.OpeningCredit), Money.Format(r.TurnoverDebit), Money.Format(r.TurnoverCredit), Money.Format(r.ClosingDebit), Money.Format(r.ClosingCredit)))
            .ToList();
        return h.Count == 0 ? null : new OpeningBalanceImportDto(h[0].Id, DateOnly.FromDateTime(h[0].StartDate), h[0].Status, h[0].FileName, h[0].OpeningEntryId,
            h[0].TurnoverEntryId, h[0].ValidatedAt, rows);
    }

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);
}
