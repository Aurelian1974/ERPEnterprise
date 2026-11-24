using Erp.Core.Contracts;
using Erp.Finance.Application.Bank;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Contracts.Settlement;
using Erp.Finance.Domain;

namespace Erp.Finance.Application.Fx;

/// <summary>O diferenta de curs realizata (macheta f4-02). Source: 1 alocare, 2 schimb valutar, 3 iesire din cont, 4 indexare.</summary>
public sealed record FxDifferenceDto(long Id, DateOnly DifferenceDate, byte Source, int? PartnerId, string? PartnerName, PartnerRole? Role, long? OpenItemId,
    string? DocumentNo, long? AllocationId, string? CounterDocumentNo, string? AccountCode, string? CounterAccountCode, string CurrencyCode, string ForeignAmount,
    string CarryingAmountRon, string SettlementAmountRon, string DifferenceRon, string GlAccountCode, bool IsIndexed, bool IsReversal, bool IsCorrection);

/// <summary>Totalul pe cont de diferenta (765, 768, 665, 668) pe interval.</summary>
public sealed record FxAccountTotalDto(string GlAccountCode, string DifferenceRon);

public sealed record FxDifferencesDto(DateOnly From, DateOnly To, string GainRon, string LossRon, string NetRon, string CorrectionsRon,
    IReadOnlyList<FxAccountTotalDto> Accounts, IReadOnlyList<FxDifferenceDto> Items);

public sealed record FxDifferenceRow(long Id, DateOnly DifferenceDate, byte Source, int? PartnerId, string? PartnerName, PartnerRole? Role, long? OpenItemId,
    string? DocumentNo, long? AllocationId, string? CounterDocumentNo, string? AccountCode, string? CounterAccountCode, string CurrencyCode, decimal ForeignAmount,
    decimal CarryingAmountRon, decimal SettlementAmountRon, decimal DifferenceRon, string GlAccountCode, bool IsIndexed, bool IsReversal, bool IsCorrection);

/// <summary>Un element al pozitiei valutare. Category: 1 conturi si case, 2 creante, 3 efecte, 4 datorii; suma cu semnul (active +, pasive −).</summary>
public sealed record FxExposureRow(byte Category, string CurrencyCode, string Name, string? Description, DateOnly? DueDate, decimal Amount, decimal CarryingAmountRon);

public sealed record FxExposureItemDto(byte Category, string Name, string? Description, DateOnly? DueDate, string Amount, string? ValueRon);

/// <summary>Pozitia pe o moneda (macheta f4-03): conturi, creante, efecte, datorii, pozitia neta, cursul zilei, valoarea in lei si impactul la +1%.</summary>
public sealed record FxExposureCurrencyDto(string CurrencyCode, string Treasury, string Receivables, string Papers, string Payables, string Net, string? Rate,
    string? NetRon, string? ImpactOnePercentRon, IReadOnlyList<FxExposureItemDto> Items);

public sealed record FxExposureDto(DateOnly AsOf, bool IncludesAdvances, string? TotalRon, string? TotalImpactRon, IReadOnlyList<FxExposureCurrencyDto> Currencies);

/// <summary>Rapoartele multivaluta (fin.usp_FxDifference_List, fin.usp_FxExposure_Report, NET-06).</summary>
public interface IFxReportStore
{
    Task<IReadOnlyList<FxDifferenceRow>> DifferencesAsync(int companyId, DateOnly from, DateOnly to, byte? source, string? currencyCode, int? partnerId,
        string? search, CancellationToken ct);

    Task<IReadOnlyList<FxExposureRow>> ExposureAsync(int companyId, DateOnly asOf, bool includeAdvances, CancellationToken ct);
}

/// <summary>Diferentele de curs realizate pe interval (F4 10; macheta f4-02), cu totalurile pe cont, castiguri, pierderi si corectiile reevaluarii.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxView)]
public sealed record GetFxDifferences(DateOnly? From = null, DateOnly? To = null, byte? Source = null, string? CurrencyCode = null, int? PartnerId = null,
    string? Search = null) : IQuery<FxDifferencesDto>;

internal sealed class GetFxDifferencesHandler(IFxReportStore store, ICurrentContext context, TimeProvider time) : IQueryHandler<GetFxDifferences, FxDifferencesDto>
{
    public async Task<FxDifferencesDto> Handle(GetFxDifferences request, CancellationToken cancellationToken)
    {
        var today = BusinessDay.Today(time);
        var to = request.To ?? today;
        var from = request.From ?? new DateOnly(to.Year, to.Month, 1);
        var rows = await store.DifferencesAsync(context.CompanyId, from, to, request.Source, string.IsNullOrWhiteSpace(request.CurrencyCode) ? null
            : request.CurrencyCode.Trim().ToUpperInvariant(), request.PartnerId, string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            cancellationToken).ConfigureAwait(false);
        var gain = rows.Where(r => r.DifferenceRon > 0).Sum(r => r.DifferenceRon);
        var loss = -rows.Where(r => r.DifferenceRon < 0).Sum(r => r.DifferenceRon);
        var accounts = rows.GroupBy(r => r.GlAccountCode).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new FxAccountTotalDto(g.Key, Money.Format(g.Sum(r => r.DifferenceRon)))).ToList();
        return new FxDifferencesDto(from, to, Money.Format(gain), Money.Format(loss), Money.Format(gain - loss),
            Money.Format(rows.Where(r => r.IsCorrection).Sum(r => r.DifferenceRon)), accounts,
            rows.Select(r => new FxDifferenceDto(r.Id, r.DifferenceDate, r.Source, r.PartnerId, r.PartnerName, r.Role, r.OpenItemId, r.DocumentNo, r.AllocationId,
                r.CounterDocumentNo, r.AccountCode, r.CounterAccountCode, r.CurrencyCode, Money.Format(r.ForeignAmount), Money.Format(r.CarryingAmountRon),
                Money.Format(r.SettlementAmountRon), Money.Format(r.DifferenceRon), r.GlAccountCode, r.IsIndexed, r.IsReversal, r.IsCorrection)).ToList());
    }
}

/// <summary>
/// Pozitia valutara la o data (F4 10; macheta f4-03): pe moneda, conturile si casele, creantele, efectele si datoriile, pozitia neta, valoarea ei
/// in lei la cursul BNR al zilei si castigul nerealizat la o apreciere a valutei cu 1%. Avansurile doar la cerere (implicit setarea firmei).
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxView)]
public sealed record GetFxExposure(DateOnly? AsOf = null, bool? IncludeAdvances = null) : IQuery<FxExposureDto>;

internal sealed class GetFxExposureHandler(IFxReportStore store, IExchangeRateProvider rates, IFinanceSettings settings, ICurrentContext context, TimeProvider time)
    : IQueryHandler<GetFxExposure, FxExposureDto>
{
    public async Task<FxExposureDto> Handle(GetFxExposure request, CancellationToken cancellationToken)
    {
        var asOf = request.AsOf ?? BusinessDay.Today(time);
        var includeAdvances = request.IncludeAdvances ?? await settings.RevalueAdvancesAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var rows = await store.ExposureAsync(context.CompanyId, asOf, includeAdvances, cancellationToken).ConfigureAwait(false);
        var currencies = new List<FxExposureCurrencyDto>();
        decimal? total = 0m;
        foreach (var group in rows.GroupBy(r => r.CurrencyCode).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            decimal? perUnit = null;
            try
            {
                perUnit = (await rates.GetRateAsync(group.Key, asOf, cancellationToken).ConfigureAwait(false)).PerUnit;
            }
            catch (ErpException e) when (e.Kind == ErpErrorKind.NotFound)
            {
            }

            decimal Sum(byte category) => group.Where(r => r.Category == category).Sum(r => r.Amount);
            var net = group.Sum(r => r.Amount);
            decimal? netRon = perUnit is { } p ? Math.Round(net * p, 2, MidpointRounding.AwayFromZero) : null;
            total = total is null || netRon is null ? null : total + netRon;
            currencies.Add(new FxExposureCurrencyDto(group.Key, Money.Format(Sum(1)), Money.Format(Sum(2)), Money.Format(Sum(3)), Money.Format(Sum(4)),
                Money.Format(net), perUnit?.ToString(System.Globalization.CultureInfo.InvariantCulture), netRon is { } n ? Money.Format(n) : null,
                netRon is { } i ? Money.Format(Math.Round(i / 100m, 2, MidpointRounding.AwayFromZero)) : null,
                group.Select(r => new FxExposureItemDto(r.Category, r.Name, r.Description, r.DueDate, Money.Format(r.Amount),
                    perUnit is { } u ? Money.Format(Math.Round(r.Amount * u, 2, MidpointRounding.AwayFromZero)) : null)).ToList()));
        }

        return new FxExposureDto(asOf, includeAdvances, total is { } t ? Money.Format(t) : null,
            total is { } ti ? Money.Format(Math.Round(currencies.Sum(c => decimal.Parse(c.ImpactOnePercentRon ?? "0", System.Globalization.CultureInfo.InvariantCulture)),
                2)) : null, currencies);
    }
}
