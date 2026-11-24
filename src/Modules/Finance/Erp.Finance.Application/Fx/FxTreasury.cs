using Erp.Core.Contracts;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Application.Treasury;
using Erp.Finance.Domain;

namespace Erp.Finance.Application.Fx;

/// <summary>Valoarea in lei a unui cont de trezorerie in valuta la o data (F4 3.4, 6): soldul, valoarea contabila si valoarea la cursul zilei.</summary>
public sealed record TreasuryRonValueDto(int TreasuryAccountId, string Code, string CurrencyCode, DateOnly AsOf, string Balance, string CarryingAmountRon,
    string? Rate, string? CurrentValueRon, string? UnrealizedRon);

public sealed record TreasuryFxValue(int TreasuryAccountId, string Code, string CurrencyCode, decimal Balance, decimal CarryingAmountRon, int MissingRates);

/// <summary>Valorile in lei ale conturilor in valuta si diferentele de schimb valutar (fin.usp_TreasuryFx_Values, fin.usp_FxExchange_*, NET-06).</summary>
public interface IFxTreasuryStore
{
    Task<IReadOnlyList<TreasuryFxValue>> ValuesAsync(int companyId, DateOnly asOf, int? accountId, CancellationToken ct);

    Task<IReadOnlyList<FxDifferenceMade>> RecordExchangeAsync(int companyId, IReadOnlyCollection<long> lineIds, int userId, CancellationToken ct);

    Task<IReadOnlyList<FxDifferenceMade>> ReverseExchangeAsync(int companyId, long lineId, DateOnly today, int userId, CancellationToken ct);
}

/// <summary>Valoarea in lei a conturilor de trezorerie in valuta la o data (raportul „Conturi de trezorerie in valuta”, F4 10).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.FxView)]
public sealed record GetTreasuryFxValues(DateOnly? AsOf = null, int? TreasuryAccountId = null) : IQuery<IReadOnlyList<TreasuryRonValueDto>>;

internal sealed class GetTreasuryFxValuesHandler(IFxTreasuryStore store, IExchangeRateProvider rates, ICurrentContext context, TimeProvider time)
    : IQueryHandler<GetTreasuryFxValues, IReadOnlyList<TreasuryRonValueDto>>
{
    public async Task<IReadOnlyList<TreasuryRonValueDto>> Handle(GetTreasuryFxValues request, CancellationToken cancellationToken)
    {
        var asOf = request.AsOf ?? BusinessDay.Today(time);
        var result = new List<TreasuryRonValueDto>();
        foreach (var v in await store.ValuesAsync(context.CompanyId, asOf, request.TreasuryAccountId, cancellationToken).ConfigureAwait(false))
        {
            decimal? perUnit = null;
            try
            {
                perUnit = (await rates.GetRateAsync(v.CurrencyCode, asOf, cancellationToken).ConfigureAwait(false)).PerUnit;
            }
            catch (ErpException e) when (e.Kind == ErpErrorKind.NotFound)
            {
            }

            var current = perUnit is { } p ? Math.Round(v.Balance * p, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
            result.Add(new TreasuryRonValueDto(v.TreasuryAccountId, v.Code, v.CurrencyCode, asOf, Money.Format(v.Balance), Money.Format(v.CarryingAmountRon),
                perUnit?.ToString(System.Globalization.CultureInfo.InvariantCulture), current is { } c ? Money.Format(c) : null,
                current is { } d ? Money.Format(d - v.CarryingAmountRon) : null));
        }

        return result;
    }
}
