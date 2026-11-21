using System.Data;
using System.Globalization;
using Dapper;
using Erp.Core.Application.Organization;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Organization;

/// <summary>Versiunile datelor firmei (core.usp_CompanyVersion_List, usp_Company_At; NET-06).</summary>
public sealed class CompanyVersionStore(IDbSession session) : ICompanyVersionStore
{
    public async Task<IReadOnlyList<CompanyVersionDto>> ListAsync(int companyId, CancellationToken ct) =>
        [.. (await session.Connection.QueryAsync<VersionRow>(Command("core.usp_CompanyVersion_List", new { CompanyId = companyId }, ct)).ConfigureAwait(false))
            .Select(v => new CompanyVersionDto(v.Id, DateOnly.FromDateTime(v.ValidFrom), v.ValidTo is { } to ? DateOnly.FromDateTime(to) : null, v.Name,
                v.VatPayer, v.VatOnCollection, v.TradeRegisterNo, v.ShareCapital?.ToString("0.00", CultureInfo.InvariantCulture), v.Address, v.City,
                v.CountyCode, v.CountryCode, v.PostalCode, v.ModifiedByName, DateTime.SpecifyKind(v.ModifiedAt, DateTimeKind.Utc)))];

    public async Task<CompanySnapshotDto?> GetAtAsync(int companyId, DateOnly date, CancellationToken ct)
    {
        var row = await session.Connection.QuerySingleOrDefaultAsync<SnapshotRow>(Command("core.usp_Company_At",
            new { CompanyId = companyId, Date = date.ToDateTime(TimeOnly.MinValue) }, ct)).ConfigureAwait(false);
        return row is null ? null : new CompanySnapshotDto(row.CompanyId, row.Code, row.FiscalCode, row.Name, row.VatPayer, row.VatOnCollection,
            row.TradeRegisterNo, row.ShareCapital, row.Address, row.City, row.CitySirutaCode, row.CountyCode, row.CountryCode, row.PostalCode,
            DateOnly.FromDateTime(row.ValidFrom));
    }

    private CommandDefinition Command(string procedure, object parameters, CancellationToken ct) =>
        new(procedure, parameters, session.Transaction, commandType: CommandType.StoredProcedure, cancellationToken: ct);

    private sealed record VersionRow(int Id, DateTime ValidFrom, DateTime? ValidTo, string Name, bool VatPayer, bool VatOnCollection, string TradeRegisterNo,
        decimal? ShareCapital, string Address, string City, string? CountyCode, string CountryCode, string? PostalCode, string ModifiedByName, DateTime ModifiedAt);

    private sealed record SnapshotRow(int CompanyId, string Code, string FiscalCode, string Name, bool VatPayer, bool VatOnCollection, string TradeRegisterNo,
        decimal? ShareCapital, string Address, string City, int? CitySirutaCode, string? CountyCode, string CountryCode, string? PostalCode, DateTime ValidFrom);
}
