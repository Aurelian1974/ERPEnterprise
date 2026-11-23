using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Fx;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Reevaluarea valutara prin procedurile fin.usp_FxRevaluation_* (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class FxRevaluationStore(IDbSession session) : IFxRevaluationStore
{
    public async Task<(IReadOnlyList<string> Currencies, (int Year, int Month)? LastValidated)> InputsAsync(int companyId, DateOnly revaluationDate,
        bool includeAdvances, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_FxRevaluation_Inputs",
            new { CompanyId = companyId, RevaluationDate = Day(revaluationDate), IncludeAdvances = includeAdvances }, ct)).ConfigureAwait(false);
        var currencies = (await grid.ReadAsync<string>().ConfigureAwait(false)).Select(c => c.Trim()).ToList();
        var last = (await grid.ReadAsync<(short Year, byte Month)>().ConfigureAwait(false)).ToList();
        return (currencies, last.Count == 0 ? null : (last[0].Year, last[0].Month));
    }

    public async Task<int> GenerateAsync(int companyId, int year, int month, DateOnly revaluationDate, DateOnly rateDate, bool includeAdvances,
        IReadOnlyList<FxRateRecord> rates, int userId, CancellationToken ct)
    {
        using var table = new DataTable();
        table.Columns.Add("CurrencyCode", typeof(string));
        table.Columns.Add("Rate", typeof(decimal));
        table.Columns.Add("Multiplier", typeof(int));
        table.Columns.Add("RateDate", typeof(DateTime));
        table.Columns.Add("Source", typeof(string));
        foreach (var r in rates)
        {
            table.Rows.Add(r.CurrencyCode, r.Rate, r.Multiplier, Day(r.RateDate), r.Source);
        }

        return await session.Connection.QuerySingleAsync<int>(Command("fin.usp_FxRevaluation_Generate", new
        {
            CompanyId = companyId, Year = (short)year, Month = (byte)month, RevaluationDate = Day(revaluationDate), RateDate = Day(rateDate),
            IncludeAdvances = includeAdvances, Rates = table.AsTableValuedParameter("fin.FxRateList"), UserId = userId,
        }, ct, 600)).ConfigureAwait(false);
    }

    public async Task<FxRunChanged> ValidateAsync(int companyId, int runId, byte[] rowVersion, int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_FxRevaluation_Validate",
            new { CompanyId = companyId, RunId = runId, RowVersion = rowVersion, UserId = userId }, ct, 600)).ConfigureAwait(false);
        var run = await grid.ReadSingleAsync<ChangedRow>().ConfigureAwait(false);
        var totals = (await grid.ReadAsync<(string GlAccountCode, string? CounterGlAccountCode, string CurrencyCode, decimal DifferenceRon)>().ConfigureAwait(false))
            .Select(t => new ReevaluareTotal(t.GlAccountCode, t.CounterGlAccountCode, t.CurrencyCode.Trim(), t.DifferenceRon)).ToList();
        var corrections = await ReadFxAsync(grid).ConfigureAwait(false);
        return run.ToChanged(totals, corrections);
    }

    public async Task<FxRunChanged> ReverseAsync(int companyId, int runId, byte[] rowVersion, DateOnly today, int userId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_FxRevaluation_Reverse",
            new { CompanyId = companyId, RunId = runId, RowVersion = rowVersion, Today = Day(today), UserId = userId }, ct, 600)).ConfigureAwait(false);
        var run = await grid.ReadSingleAsync<ChangedRow>().ConfigureAwait(false);
        var corrections = await ReadFxAsync(grid).ConfigureAwait(false);
        return run.ToChanged([], corrections);
    }

    public Task DeleteAsync(int companyId, int runId, byte[] rowVersion, CancellationToken ct) =>
        session.Connection.ExecuteAsync(Command("fin.usp_FxRevaluation_Delete", new { CompanyId = companyId, RunId = runId, RowVersion = rowVersion }, ct));

    public async Task<IReadOnlyList<FxRunItemDto>> ListAsync(int companyId, int? year, CancellationToken ct) =>
        (await session.Connection.QueryAsync<ListRow>(Command("fin.usp_FxRevaluation_List", new { CompanyId = companyId, Year = (short?)year }, ct))
            .ConfigureAwait(false))
        .Select(r => new FxRunItemDto(r.Id, r.Number, r.PeriodYear, r.PeriodMonth, DateOnly.FromDateTime(r.RevaluationDate), DateOnly.FromDateTime(r.RateDate),
            (FxRunStatus)r.Status, Money.Format(r.TotalGainRon), Money.Format(r.TotalLossRon), Money.Format(r.NetRon), r.LineCount, r.IsStale, r.GeneratedAt,
            r.ValidatedAt, r.ReversedAt)).ToList();

    public async Task<FxRunDto?> GetAsync(int companyId, int runId, CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(Command("fin.usp_FxRevaluation_Get", new { CompanyId = companyId, RunId = runId }, ct))
            .ConfigureAwait(false);
        var h = await grid.ReadSingleOrDefaultAsync<HeaderRow>().ConfigureAwait(false);
        if (h is null)
        {
            return null;
        }

        var rates = (await grid.ReadAsync<(string CurrencyCode, decimal Rate, int Multiplier, DateTime RateDate, string Source)>().ConfigureAwait(false))
            .Select(r => new FxRunRateDto(r.CurrencyCode.Trim(), Rate(r.Rate), r.Multiplier, DateOnly.FromDateTime(r.RateDate), r.Source)).ToList();
        var currencies = (await grid.ReadAsync<(string CurrencyCode, int Lines, decimal ForeignAmount, decimal CarryingAmountRon, decimal RevaluedAmountRon,
                decimal GainRon, decimal LossRon)>().ConfigureAwait(false))
            .Select(c => new FxRunCurrencyDto(c.CurrencyCode.Trim(), c.Lines, Money.Format(c.ForeignAmount), Money.Format(c.CarryingAmountRon),
                Money.Format(c.RevaluedAmountRon), Money.Format(c.GainRon), Money.Format(c.LossRon))).ToList();
        var lines = (await grid.ReadAsync<LineRow>().ConfigureAwait(false))
            .Select(l => new FxRunLineDto(l.Id, l.ElementKind, l.OpenItemId, l.DocumentNo, l.PartnerId, l.PartnerName, (PartnerRole?)l.Role, l.CurrencyCode.Trim(),
                Money.Format(l.ForeignAmount), Rate(l.Rate), Money.Format(l.CarryingAmountRon), Money.Format(l.RevaluedAmountRon), Money.Format(l.DifferenceRon),
                l.GlAccountCode, l.CounterGlAccountCode)).ToList();
        return new FxRunDto(h.Id, h.Number, h.PeriodYear, h.PeriodMonth, DateOnly.FromDateTime(h.RevaluationDate), DateOnly.FromDateTime(h.RateDate),
            (FxRunStatus)h.Status, h.IncludesAdvances, Money.Format(h.TotalGainRon), Money.Format(h.TotalLossRon), h.GeneratedAt, h.ValidatedAt, h.ValidatedByName,
            h.ReversedAt, h.LineCount, rates, currencies, lines, h.RowVersion);
    }

    private static async Task<IReadOnlyList<FxDifferenceMade>> ReadFxAsync(SqlMapper.GridReader grid) =>
        (await grid.ReadAsync<FxRow>().ConfigureAwait(false))
        .Select(x => new FxDifferenceMade(x.Id, DateOnly.FromDateTime(x.DifferenceDate), x.Source, x.AllocationId, x.PartnerId, (PartnerRole?)x.Role, x.OpenItemId,
            x.CurrencyCode.Trim(), x.ForeignAmount, x.DifferenceRon, x.IsIndexed, x.GlAccountCode, x.IsReversal)).ToList();

    private static string Rate(decimal value) => (value / 1.000000000000000000000000000000000m).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct, int? timeout = null) =>
        new(name, parameters, session.Transaction, commandTimeout: timeout, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record ChangedRow(int Id, string Number, short PeriodYear, byte PeriodMonth, DateTime RevaluationDate, decimal TotalGainRon, decimal TotalLossRon,
        byte[] RowVersion, int LineCount)
    {
        public FxRunChanged ToChanged(IReadOnlyList<ReevaluareTotal> totals, IReadOnlyList<FxDifferenceMade> corrections) =>
            new(Id, Number, PeriodYear, PeriodMonth, DateOnly.FromDateTime(RevaluationDate), LineCount, RowVersion, totals, corrections);
    }

    private sealed record FxRow(long Id, DateTime DifferenceDate, byte Source, long? AllocationId, int? PartnerId, byte? Role, long? OpenItemId, string CurrencyCode,
        decimal ForeignAmount, decimal DifferenceRon, bool IsIndexed, string GlAccountCode, bool IsReversal);

    private sealed record ListRow(int Id, string Number, short PeriodYear, byte PeriodMonth, DateTime RevaluationDate, DateTime RateDate, byte Status,
        decimal TotalGainRon, decimal TotalLossRon, decimal NetRon, int LineCount, bool IsStale, DateTime GeneratedAt, DateTime? ValidatedAt, DateTime? ReversedAt);

    private sealed record HeaderRow(int Id, string Number, short PeriodYear, byte PeriodMonth, DateTime RevaluationDate, DateTime RateDate, byte Status,
        bool IncludesAdvances, decimal TotalGainRon, decimal TotalLossRon, DateTime GeneratedAt, DateTime? ValidatedAt, string? ValidatedByName, DateTime? ReversedAt,
        byte[] RowVersion, int LineCount);

    private sealed record LineRow(long Id, byte ElementKind, long? OpenItemId, string? DocumentNo, int? PartnerId, string? PartnerName, byte? Role, string CurrencyCode,
        decimal ForeignAmount, decimal Rate, decimal CarryingAmountRon, decimal RevaluedAmountRon, decimal DifferenceRon, string GlAccountCode,
        string? CounterGlAccountCode);
}
