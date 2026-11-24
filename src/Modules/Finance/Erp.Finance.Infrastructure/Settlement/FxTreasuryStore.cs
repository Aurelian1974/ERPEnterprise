using System.Data;
using Dapper;
using Erp.Core.Contracts;
using Erp.Finance.Application.Fx;
using Erp.Finance.Application.Settlement;
using Erp.Finance.Contracts.Settlement;

namespace Erp.Finance.Infrastructure.Settlement;

/// <summary>Valorile in lei ale conturilor in valuta si diferentele de schimb valutar (NET-06), in tranzactia pipeline-ului.</summary>
public sealed class FxTreasuryStore(IDbSession session) : IFxTreasuryStore
{
    public async Task<IReadOnlyList<TreasuryFxValue>> ValuesAsync(int companyId, DateOnly asOf, int? accountId, CancellationToken ct) =>
        (await session.Connection.QueryAsync<(int TreasuryAccountId, string Code, byte Kind, string CurrencyCode, string GlAccountCode, decimal Balance,
            decimal CarryingAmountRon, int MissingRates)>(Command("fin.usp_TreasuryFx_Values",
            new { CompanyId = companyId, AsOf = asOf.ToDateTime(TimeOnly.MinValue), AccountId = accountId }, ct)).ConfigureAwait(false))
        .Select(v => new TreasuryFxValue(v.TreasuryAccountId, v.Code, v.CurrencyCode.Trim(), v.Balance, v.CarryingAmountRon, v.MissingRates)).ToList();

    public async Task<IReadOnlyList<FxDifferenceMade>> RecordExchangeAsync(int companyId, IReadOnlyCollection<long> lineIds, int userId, CancellationToken ct) =>
        Map(await session.Connection.QueryAsync<FxRow>(Command("fin.usp_FxExchange_Record",
            new { CompanyId = companyId, LineIds = System.Text.Json.JsonSerializer.Serialize(lineIds), UserId = userId }, ct)).ConfigureAwait(false));

    public async Task<IReadOnlyList<FxDifferenceMade>> ReverseExchangeAsync(int companyId, long lineId, DateOnly today, int userId, CancellationToken ct) =>
        Map(await session.Connection.QueryAsync<FxRow>(Command("fin.usp_FxExchange_Reverse",
            new { CompanyId = companyId, LineId = lineId, Today = today.ToDateTime(TimeOnly.MinValue), UserId = userId }, ct)).ConfigureAwait(false));

    private static List<FxDifferenceMade> Map(IEnumerable<FxRow> rows) =>
        rows.Select(x => new FxDifferenceMade(x.Id, DateOnly.FromDateTime(x.DifferenceDate), x.Source, x.AllocationId, x.PartnerId, (PartnerRole?)x.Role, x.OpenItemId,
            x.CurrencyCode.Trim(), x.ForeignAmount, x.DifferenceRon, x.IsIndexed, x.GlAccountCode, x.IsReversal)).ToList();

    private CommandDefinition Command(string name, object parameters, CancellationToken ct) =>
        new(name, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record FxRow(long Id, DateTime DifferenceDate, byte Source, long? AllocationId, int? PartnerId, byte? Role, long? OpenItemId, string CurrencyCode,
        decimal ForeignAmount, decimal DifferenceRon, bool IsIndexed, string GlAccountCode, bool IsReversal);
}
