using System.Data;
using Dapper;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Organization;

/// <summary>Nomenclatorul SIRUTA prin core.usp_City_Search (NET-06).</summary>
public sealed class CityStore(IDbSession session) : ICityStore
{
    public async Task<IReadOnlyList<CityOptionDto>> SearchAsync(string countyCode, string? search, int? sirutaCode, CancellationToken ct) =>
        [.. await session.Connection.QueryAsync<CityOptionDto>(new CommandDefinition("core.usp_City_Search", new
        {
            CountyCode = new DbString { Value = countyCode, Length = 3 }, Search = new DbString { Value = search, Length = 100 }, SirutaCode = sirutaCode,
        }, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct)).ConfigureAwait(false)];
}
