using System.Data;
using Dapper;
using Erp.Core.Application.Abstractions;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Precision;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Precision;

/// <summary>Politicile de zecimale prin core.usp_PrecisionPolicy_* (NET-06).</summary>
public sealed class PrecisionStore(IDbSession session) : IPrecisionStore
{
    public async Task<PrecisionPageDto> ListAsync(CancellationToken ct)
    {
        using var grid = await session.Connection.QueryMultipleAsync(new CommandDefinition("core.usp_PrecisionPolicy_List", null, session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false);
        var policies = (await grid.ReadAsync<PrecisionPolicyItemDto>().ConfigureAwait(false)).ToList();
        var companies = (await grid.ReadAsync<PrecisionUsageDto>().ConfigureAwait(false)).ToList();
        var items = await grid.ReadSingleAsync<int>().ConfigureAwait(false);
        return new PrecisionPageDto(policies, companies, items);
    }

    public Task<SavedEntity> SaveAsync(SavePrecisionPolicy policy, int userId, CancellationToken ct) =>
        session.Connection.QuerySingleAsync<SavedEntity>(new CommandDefinition("core.usp_PrecisionPolicy_Save", new
        {
            policy.Id, policy.Code, policy.Name, policy.PricePrecision, policy.QuantityPrecision, policy.RoundingMode, policy.IsActive, policy.RowVersion,
            UserId = userId,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct));
}

/// <summary>IPrecisionService (02.01, 6): precizia din core.usp_Precision_Resolve, rotunjirea din PrecisionRounding.</summary>
public sealed class PrecisionService(IDbSession session, ISqlErrorTranslator errors) : IPrecisionService
{
    public async Task<PrecisionInfo> ResolveAsync(int companyId, int? itemId, int? priceListId, CancellationToken ct)
    {
        try
        {
            var row = await session.Connection.QuerySingleAsync<Row>(new CommandDefinition("core.usp_Precision_Resolve",
                new { CompanyId = companyId, ItemId = itemId }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct))
                .ConfigureAwait(false);
            return new PrecisionInfo(row.PriceDecimals, row.QuantityDecimals, row.AmountDecimals, (MidpointRounding)row.RoundingMode);
        }
        catch (Exception exception) when (errors.Translate(exception) is { } translated)
        {
            throw translated;
        }
    }

    public decimal RoundPrice(decimal value, PrecisionInfo precision) => PrecisionRounding.Price(value, precision);

    public decimal RoundQuantity(decimal value, PrecisionInfo precision) => PrecisionRounding.Quantity(value, precision);

    public decimal RoundAmount(decimal value, PrecisionInfo precision) => PrecisionRounding.Amount(value, precision);

    private sealed record Row(byte PriceDecimals, byte QuantityDecimals, byte AmountDecimals, byte RoundingMode);
}
