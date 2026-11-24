using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Fx;
using Erp.Finance.Contracts.Settlement;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Rapoartele multivaluta prin fin.usp_FxDifference_List si fin.usp_FxExposure_Report (NET-06).</summary>
public sealed class FxReportStore(IDbSession session) : IFxReportStore
{
    public async Task<IReadOnlyList<FxDifferenceRow>> DifferencesAsync(int companyId, DateOnly from, DateOnly to, byte? source, string? currencyCode, int? partnerId,
        string? search, CancellationToken ct) =>
        (await session.Connection.QueryAsync<Row>(Command("fin.usp_FxDifference_List", new
        {
            CompanyId = companyId, From = Day(from), To = Day(to), Source = source, CurrencyCode = currencyCode, PartnerId = partnerId, Search = search,
        }, ct)).ConfigureAwait(false))
        .Select(r => new FxDifferenceRow(r.Id, DateOnly.FromDateTime(r.DifferenceDate), r.Source, r.PartnerId, r.PartnerName, (PartnerRole?)r.Role, r.OpenItemId,
            r.DocumentNo, r.AllocationId, r.CounterDocumentNo, r.AccountCode, r.CounterAccountCode, r.CurrencyCode.Trim(), r.ForeignAmount, r.CarryingAmountRon,
            r.SettlementAmountRon, r.DifferenceRon, r.GlAccountCode, r.IsIndexed, r.IsReversal, r.IsCorrection)).ToList();

    public async Task<IReadOnlyList<FxExposureRow>> ExposureAsync(int companyId, DateOnly asOf, bool includeAdvances, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(byte Category, string CurrencyCode, string Name, string? Description, DateTime? DueDate, decimal Amount,
            decimal CarryingAmountRon)>(Command("fin.usp_FxExposure_Report", new { CompanyId = companyId, AsOf = Day(asOf), IncludeAdvances = includeAdvances }, ct))
            .ConfigureAwait(false))
        .Select(r => new FxExposureRow(r.Category, r.CurrencyCode.Trim(), r.Name, r.Description, r.DueDate is { } d ? DateOnly.FromDateTime(d) : null, r.Amount,
            r.CarryingAmountRon)).ToList();

    private static DateTime Day(DateOnly day) => day.ToDateTime(TimeOnly.MinValue);

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record Row(long Id, DateTime DifferenceDate, byte Source, int? PartnerId, string? PartnerName, byte? Role, long? OpenItemId, string? DocumentNo,
        long? AllocationId, string? CounterDocumentNo, string? AccountCode, string? CounterAccountCode, string CurrencyCode, decimal ForeignAmount,
        decimal CarryingAmountRon, decimal SettlementAmountRon, decimal DifferenceRon, string GlAccountCode, bool IsIndexed, bool IsReversal, bool IsCorrection);
}
