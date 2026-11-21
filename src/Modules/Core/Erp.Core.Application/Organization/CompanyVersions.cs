using Erp.Core.Contracts;

namespace Erp.Core.Application.Organization;

/// <summary>O versiune a datelor firmei (02.01, 3.6 punctul 2); ValidTo null = versiunea curenta sau programata.</summary>
public sealed record CompanyVersionDto(int Id, DateOnly ValidFrom, DateOnly? ValidTo, string Name, bool VatPayer, bool VatOnCollection,
    string TradeRegisterNo, string? ShareCapital, string Address, string City, string? CountyCode, string CountryCode, string? PostalCode,
    string ModifiedByName, DateTime ModifiedAt);

/// <summary>Accesul la versiunile firmei (core.usp_CompanyVersion_List, usp_Company_At; NET-06).</summary>
public interface ICompanyVersionStore
{
    Task<IReadOnlyList<CompanyVersionDto>> ListAsync(int companyId, CancellationToken ct);

    Task<CompanySnapshotDto?> GetAtAsync(int companyId, DateOnly date, CancellationToken ct);
}

/// <summary>Tab-ul „Date pe perioade” al firmei: versiunile, inclusiv cele programate.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.CompanyView)]
public sealed record GetCompanyVersions(int Id) : IQuery<IReadOnlyList<CompanyVersionDto>>;

internal sealed class GetCompanyVersionsHandler(ICompanyVersionStore store, ICurrentContext context)
    : IQueryHandler<GetCompanyVersions, IReadOnlyList<CompanyVersionDto>>
{
    public Task<IReadOnlyList<CompanyVersionDto>> Handle(GetCompanyVersions request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.Id);
        return store.ListAsync(request.Id, cancellationToken);
    }
}

/// <summary>ICompanyDirectory (02.01, 6) peste core.usp_Company_At.</summary>
public sealed class CompanyDirectory(ICompanyVersionStore store) : ICompanyDirectory
{
    public async Task<CompanySnapshotDto> GetAtAsync(int companyId, DateOnly date, CancellationToken ct) =>
        await store.GetAtAsync(companyId, date, ct).ConfigureAwait(false) ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Firma nu există.");
}
