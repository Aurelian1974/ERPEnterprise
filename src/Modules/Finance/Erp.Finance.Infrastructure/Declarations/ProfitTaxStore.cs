using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Declarations;
using Erp.Finance.Application.Periods;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Declarations;

namespace Erp.Finance.Infrastructure.Declarations;

/// <summary>Fisa impozitului pe profit prin fin.usp_ProfitTax_Data, _Save, _Get, _SetPosted si nota prin fin.usp_SystemEntry_Write.</summary>
public sealed class ProfitTaxStore(IDbSession session) : IProfitTaxStore
{
    public async Task<ProfitTaxData> DataAsync(int companyId, int year, byte quarter, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_ProfitTax_Data", new { CompanyId = companyId, Year = (short)year, Quarter = quarter },
            ct)).ConfigureAwait(false);
        var turnover = (await grid.ReadAsync<(string Class, byte TaxTreatment, decimal Net)>().ConfigureAwait(false))
            .Select(t => new ProfitTaxTurnover(t.Class[0], t.TaxTreatment, t.Net)).ToList();
        var h = await grid.ReadSingleAsync<Header>().ConfigureAwait(false);
        var manual = (await grid.ReadAsync<(string RowCode, decimal Amount, string? Reason)>().ConfigureAwait(false))
            .Select(m => new ProfitTaxManualRow(m.RowCode, m.Amount, m.Reason ?? string.Empty)).ToList();
        var accounts = (await grid.ReadAsync<(int Id, string Code)>().ConfigureAwait(false)).ToDictionary(a => a.Code, a => a.Id);
        return new ProfitTaxData(turnover, DateOnly.FromDateTime(h.YearStart), DateOnly.FromDateTime(h.QuarterEnd), h.LastMonthStatus, h.Regime, h.Rate, h.PriorTax,
            manual, accounts.TryGetValue("691", out var e) ? e : null, accounts.TryGetValue("4411", out var p) ? p : null);
    }

    public Task<int> SaveAsync(int companyId, int year, byte quarter, byte regime, decimal rate, ProfitTaxResult result, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<int>(Command("fin.usp_ProfitTax_Save", new
        {
            CompanyId = companyId, Year = (short)year, Quarter = quarter, Regime = regime, TaxRate = rate, result.TaxDue, UserId = userId,
            Lines = JsonSerializer.Serialize(result.Lines.Select(l => new
            {
                rowCode = l.RowCode, label = l.Label, sortOrder = l.SortOrder, isManual = l.IsManual, amount = l.Amount, reason = l.Reason,
            })),
        }, ct));

    public async Task<ProfitTaxDto?> GetAsync(int companyId, int? year, byte? quarter, DateOnly? periodTo, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_ProfitTax_Get", new
        {
            CompanyId = companyId, Year = (short?)year, Quarter = quarter, PeriodTo = periodTo?.ToDateTime(TimeOnly.MinValue),
        }, ct)).ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<Row>().ConfigureAwait(false);
        var lines = (await grid.ReadAsync<(string RowCode, string Label, int SortOrder, bool IsManual, decimal Amount, string? Reason)>().ConfigureAwait(false))
            .Select(l => new ProfitTaxLineDto(l.RowCode, l.Label, l.IsManual, Money.Format(l.Amount), l.Reason)).ToList();
        return h is null ? null : new ProfitTaxDto(h.Id, h.Year, h.Quarter, h.Regime, h.TaxRate, Money.Format(h.TaxDue), h.EntryId, h.EntryNumber,
            Money.Format(h.PostedAmount), h.CalculatedAt, h.PostedAt, h.Hash, lines);
    }

    public async Task<long> WriteEntryAsync(int companyId, long? entryId, DateOnly date, int computationId, IReadOnlyList<SystemLine> lines, string reason, int userId,
        CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleAsync<(long EntryId, bool IsNew, int OldVersion, decimal OldTotal)>(Command("fin.usp_SystemEntry_Write", new
        {
            CompanyId = companyId, EntryId = entryId, AccountingDate = date.ToDateTime(TimeOnly.MinValue), SourceKind = (byte)4,
            SourceDocumentType = "Finance.ProfitTax", SourceDocumentId = (long)computationId, Description = reason, Reason = reason, UserId = userId,
            Lines = JsonSerializer.Serialize(lines.Select(l => new { accountId = l.AccountId, side = l.Side, amountRon = l.AmountRon })),
        }, ct)).ConfigureAwait(false);
        return r.EntryId;
    }

    public Task SetPostedAsync(int companyId, int id, long entryId, decimal amount, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_ProfitTax_SetPosted", new { CompanyId = companyId, Id = id, EntryId = entryId, Amount = amount, UserId = userId },
            ct));

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record Header(DateTime YearStart, DateTime QuarterEnd, byte? LastMonthStatus, byte Regime, decimal? Rate, decimal PriorTax);

    private sealed record Row(int Id, short Year, byte Quarter, byte Regime, decimal TaxRate, decimal TaxDue, long? EntryId, int? EntryNumber, decimal? PostedAmount,
        DateTime CalculatedAt, DateTime? PostedAt, int Hash);
}
