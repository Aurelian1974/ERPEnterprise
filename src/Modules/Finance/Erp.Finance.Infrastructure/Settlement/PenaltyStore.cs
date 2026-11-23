using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Settlement;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Penalitatile si ratele de referinta prin procedurile fin.usp_Penalty*, fin.usp_ReferenceRate_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class PenaltyStore(IDbSession session) : IPenaltyStore
{
    public async Task<PenaltySources> SourcesAsync(int companyId, int? partnerId, DateOnly from, DateOnly to, int? excludeCalculationId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_Penalty_Sources", new
        {
            CompanyId = companyId, PartnerId = partnerId, PeriodFrom = Day(from), PeriodTo = Day(to), ExcludeCalculationId = excludeCalculationId,
        }, ct)).ConfigureAwait(false);
        var items = (await grid.ReadAsync<SourceRow>().ConfigureAwait(false))
            .Select(r => new PenaltySourceItem(r.OpenItemId, r.PartnerId, r.PartnerName, r.DocumentNo, DateOnly.FromDateTime(r.DueDate), r.Amount, r.CurrencyCode,
                r.RatePerDay, r.GraceDays, r.CapAtPrincipal, r.ApplyFixedRecoveryCost, r.AlreadyCharged, r.ChargedThrough is { } c ? DateOnly.FromDateTime(c) : null,
                r.HasFixedCost)).ToList();
        var payments = (await grid.ReadAsync<(long OpenItemId, DateTime AllocationDate, decimal Amount)>().ConfigureAwait(false))
            .ToLookup(p => p.OpenItemId, p => (DateOnly.FromDateTime(p.AllocationDate), p.Amount));
        var disputed = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        var rates = (await grid.ReadAsync<(DateTime ValidFrom, decimal Rate)>().ConfigureAwait(false))
            .Select(r => new ReferenceRate(DateOnly.FromDateTime(r.ValidFrom), r.Rate)).ToList();
        return new PenaltySources(items, payments, disputed, rates);
    }

    public async Task<FinanceSaved> SaveAsync(int companyId, int? id, string? number, DateOnly calculationDate, DateOnly from, DateOnly to, int? partnerId,
        int disputedCount, IReadOnlyList<PenaltyLineRecord> lines, byte[]? rowVersion, int userId, CancellationToken ct)
    {
        using var table = new DataTable();
        table.Columns.Add("LineNumber", typeof(int));
        table.Columns.Add("Kind", typeof(byte));
        table.Columns.Add("PartnerId", typeof(int));
        table.Columns.Add("OpenItemId", typeof(long));
        table.Columns.Add("DueDate", typeof(DateTime));
        table.Columns.Add("FromDate", typeof(DateTime));
        table.Columns.Add("ToDate", typeof(DateTime));
        table.Columns.Add("Days", typeof(int));
        table.Columns.Add("BaseAmount", typeof(decimal));
        table.Columns.Add("CurrencyCode", typeof(string));
        table.Columns.Add("RatePerDay", typeof(decimal));
        table.Columns.Add("AnnualRate", typeof(decimal));
        table.Columns.Add("PenaltyAmount", typeof(decimal));
        foreach (var l in lines)
        {
            table.Rows.Add(l.LineNumber, (byte)l.Kind, l.PartnerId, l.OpenItemId, Day(l.DueDate), l.FromDate is { } f ? Day(f) : DBNull.Value,
                l.ToDate is { } t ? Day(t) : DBNull.Value, l.Days, l.BaseAmount, l.CurrencyCode, l.RatePerDay is { } r ? r : DBNull.Value,
                l.AnnualRate is { } a ? a : DBNull.Value, l.PenaltyAmount);
        }

        var saved = await session.Connection.QuerySingleAsync<(int Id, byte[] RowVersion)>(Command("fin.usp_PenaltyCalculation_Save", new
        {
            Id = id, CompanyId = companyId, Number = number, CalculationDate = Day(calculationDate), PeriodFrom = Day(from), PeriodTo = Day(to), PartnerId = partnerId,
            DisputedCount = disputedCount, Lines = table.AsTableValuedParameter("fin.PenaltyLineList"), RowVersion = rowVersion, UserId = userId,
        }, ct)).ConfigureAwait(false);
        return new FinanceSaved(saved.Id, saved.RowVersion);
    }

    public Task<byte[]> ChangeAsync(int companyId, int id, PenaltyStatus? status, IReadOnlyList<long>? removeLines, byte[] rowVersion, int userId,
        CancellationToken ct) =>
        session.Connection.QuerySingleAsync<byte[]>(Command("fin.usp_PenaltyCalculation_Change", new
        {
            Id = id, CompanyId = companyId, Status = (byte?)status, RemoveLines = removeLines is null ? null : JsonSerializer.Serialize(removeLines),
            RowVersion = rowVersion, UserId = userId,
        }, ct));

    public async Task<PenaltyCalculationDto?> GetAsync(int companyId, int id, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PenaltyCalculation_Get", new { CompanyId = companyId, Id = id }, ct))
            .ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>().ConfigureAwait(false);
        if (h is null)
        {
            return null;
        }

        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false))
            .Select(l => new PenaltyLineDto(l.Id, l.LineNumber, (PenaltyLineKind)l.Kind, l.PartnerId, l.PartnerName, l.OpenItemId, l.DocumentNo,
                DateOnly.FromDateTime(l.DueDate), l.FromDate is { } f ? DateOnly.FromDateTime(f) : null, l.ToDate is { } t ? DateOnly.FromDateTime(t) : null, l.Days,
                Money.Format(l.BaseAmount), l.CurrencyCode, Rate(l.RatePerDay), Rate(l.AnnualRate), Money.Format(l.PenaltyAmount))).ToList();
        var totals = lines.GroupBy(l => l.CurrencyCode).OrderBy(g => g.Key == "RON" ? 0 : 1).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CurrencyTotalDto(g.Key, Money.Format(g.Sum(l => decimal.Parse(l.PenaltyAmount, CultureInfo.InvariantCulture))))).ToList();
        return new PenaltyCalculationDto(h.Id, h.Number, DateOnly.FromDateTime(h.CalculationDate), DateOnly.FromDateTime(h.PeriodFrom), DateOnly.FromDateTime(h.PeriodTo),
            h.PartnerId, h.PartnerName, (PenaltyStatus)h.Status, h.DisputedCount, h.ApprovedAt, h.ApprovedByName, h.CreatedAt, h.CreatedByName, totals, lines,
            h.RowVersion);
    }

    public async Task<IReadOnlyList<PenaltyCalculationItemDto>> ListAsync(int companyId, int? year, PenaltyStatus? status, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_PenaltyCalculation_List",
            new { CompanyId = companyId, Year = year, Status = (byte?)status }, ct)).ConfigureAwait(false);
        var rows = (await grid.ReadAsync<ListRow>().ConfigureAwait(false)).ToList();
        var totals = (await grid.ReadAsync<(int CalculationId, string CurrencyCode, decimal Total)>().ConfigureAwait(false))
            .ToLookup(t => t.CalculationId, t => new CurrencyTotalDto(t.CurrencyCode, Money.Format(t.Total)));
        return rows.Select(r => new PenaltyCalculationItemDto(r.Id, r.Number, DateOnly.FromDateTime(r.CalculationDate), DateOnly.FromDateTime(r.PeriodFrom),
            DateOnly.FromDateTime(r.PeriodTo), r.PartnerId, r.PartnerName, (PenaltyStatus)r.Status, r.PartnerCount, totals[r.Id].ToList())).ToList();
    }

    public async Task<IReadOnlyList<ReferenceRateDto>> ReferenceRatesAsync(CancellationToken ct) =>
        (await session.Connection.QueryAsync<(DateTime ValidFrom, decimal Rate)>(Command("fin.usp_ReferenceRate_List", new { }, ct)).ConfigureAwait(false))
        .Select(r => new ReferenceRateDto(DateOnly.FromDateTime(r.ValidFrom), Rate(r.Rate)!)).ToList();

    public Task SaveReferenceRateAsync(DateOnly validFrom, decimal rate, bool delete, int userId, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_ReferenceRate_Save", new { ValidFrom = Day(validFrom), Rate = rate, Delete = delete, UserId = userId }, ct));

    /// <summary>Ratele fara zerourile de la sfarsit: 0,100000 → „0.1”, 6,5000 → „6.5”.</summary>
    private static string? Rate(decimal? value) => value is { } v ? (v / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture) : null;

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record SourceRow(long OpenItemId, int PartnerId, string PartnerName, string DocumentNo, DateTime DueDate, decimal Amount, string CurrencyCode,
        decimal RatePerDay, int GraceDays, bool CapAtPrincipal, bool ApplyFixedRecoveryCost, decimal AlreadyCharged, DateTime? ChargedThrough, bool HasFixedCost);

    private sealed record HeaderRow(int Id, string Number, DateTime CalculationDate, DateTime PeriodFrom, DateTime PeriodTo, int? PartnerId, string? PartnerName,
        byte Status, int DisputedCount, DateTime? ApprovedAt, string? ApprovedByName, DateTime CreatedAt, string? CreatedByName, byte[] RowVersion);

    private sealed record LineRow(long Id, int LineNumber, byte Kind, int PartnerId, string PartnerName, long OpenItemId, string DocumentNo, DateTime DueDate,
        DateTime? FromDate, DateTime? ToDate, int Days, decimal BaseAmount, string CurrencyCode, decimal? RatePerDay, decimal? AnnualRate, decimal PenaltyAmount);

    private sealed record ListRow(int Id, string Number, DateTime CalculationDate, DateTime PeriodFrom, DateTime PeriodTo, int? PartnerId, string? PartnerName,
        byte Status, int PartnerCount);
}
