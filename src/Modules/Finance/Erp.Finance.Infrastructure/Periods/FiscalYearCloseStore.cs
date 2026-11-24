using System.Data;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Periods;

namespace Erp.Finance.Infrastructure.Periods;

/// <summary>Inchiderea anului prin fin.usp_FiscalYear_CloseData, fin.usp_SystemEntry_Write, fin.usp_FiscalYearCloseEntry_Save, fin.usp_FiscalYear_SetStatus.</summary>
public sealed class FiscalYearCloseStore(IDbSession session) : IFiscalYearCloseStore
{
    public async Task<YearCloseData> DataAsync(int companyId, int year, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_FiscalYear_CloseData", new { CompanyId = companyId, Year = (short)year }, ct))
            .ConfigureAwait(false);
        var h = await grid.ReadSingleAsync<Header>().ConfigureAwait(false);
        var entries = (await grid.ReadAsync<(byte Kind, long EntryId)>().ConfigureAwait(false)).ToDictionary(e => e.Kind, e => e.EntryId);
        var carried = (await grid.ReadAsync<CarriedBalance>().ConfigureAwait(false)).ToList();
        return new YearCloseData(h.YearId, DateOnly.FromDateTime(h.StartDate), DateOnly.FromDateTime(h.EndDate), h.OpenMonths, h.NextYearId,
            h.NextStart is { } n ? DateOnly.FromDateTime(n) : null, h.Status, h.GrossProfit ?? 0, h.ShareCapital ?? 0, h.ExistingReserve ?? 0, entries, carried);
    }

    public async Task<ClosingWrite> WriteAsync(int companyId, long? entryId, DateOnly date, byte sourceKind, int yearId, string description,
        IReadOnlyList<SystemLine> lines, string reason, int userId, CancellationToken ct)
    {
        var r = await session.Connection.QuerySingleAsync<(long EntryId, bool IsNew, int OldVersion, decimal OldTotal)>(Command("fin.usp_SystemEntry_Write", new
        {
            CompanyId = companyId, EntryId = entryId, AccountingDate = date.ToDateTime(TimeOnly.MinValue), SourceKind = sourceKind,
            SourceDocumentType = "Finance.FiscalYearClose", SourceDocumentId = (long)yearId, Description = description, Reason = reason, UserId = userId,
            Lines = JsonSerializer.Serialize(lines.Select(l => new
            {
                accountId = l.AccountId, side = l.Side, amountRon = l.AmountRon, partnerId = l.PartnerId, employeeId = l.EmployeeId, currencyCode = l.CurrencyCode,
                amountCurrency = l.AmountCurrency,
            })),
        }, ct)).ConfigureAwait(false);
        return new ClosingWrite(r.EntryId, r.IsNew, r.OldVersion, r.OldTotal);
    }

    public Task LinkAsync(int companyId, int yearId, byte kind, long entryId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_FiscalYearCloseEntry_Save", new { CompanyId = companyId, YearId = yearId, Kind = kind, EntryId = entryId }, ct));

    public Task SetStatusAsync(int companyId, int yearId, byte status, string? reason, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_FiscalYear_SetStatus", new
        {
            CompanyId = companyId, YearId = yearId, Status = status, Reason = reason, UserId = userId,
        }, ct));

    private CommandDefinition Command(string name, object? parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record Header(int YearId, DateTime StartDate, DateTime EndDate, int OpenMonths, int? NextYearId, DateTime? NextStart, byte Status,
        decimal? GrossProfit, decimal? ShareCapital, decimal? ExistingReserve);
}
