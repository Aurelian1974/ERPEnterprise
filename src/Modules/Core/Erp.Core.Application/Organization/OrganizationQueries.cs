using Erp.Core.Contracts;

namespace Erp.Core.Application.Organization;

/// <summary>Arborele structurii (macheta 03); filtrarea dupa Core.Company.View se face in SQL, pe fiecare firma.</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record GetOrgTree : IQuery<OrgTreeDto>;

/// <summary>Fisa firmei cu conturile bancare; firma trebuie sa fie firma curenta (sau contextul instalarii).</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.CompanyView)]
public sealed record GetCompany(int Id) : IQuery<CompanyDetailDto>;

/// <summary>Tari, judete, politici de zecimale si tipuri de documente pentru formulare.</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record GetOrgLookups : IQuery<OrgLookupsDto>;

internal sealed class GetOrgTreeHandler(IOrganizationStore store, ICurrentContext context) : IQueryHandler<GetOrgTree, OrgTreeDto>
{
    public Task<OrgTreeDto> Handle(GetOrgTree request, CancellationToken cancellationToken) => store.GetTreeAsync(context.UserId, cancellationToken);
}

internal sealed class GetCompanyHandler(IOrganizationStore store, ICurrentContext context) : IQueryHandler<GetCompany, CompanyDetailDto>
{
    public async Task<CompanyDetailDto> Handle(GetCompany request, CancellationToken cancellationToken)
    {
        CompanyScope.Ensure(context, request.Id);
        return await store.GetCompanyAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Firma nu există.");
    }
}

internal sealed class GetOrgLookupsHandler(IOrganizationStore store) : IQueryHandler<GetOrgLookups, OrgLookupsDto>
{
    public Task<OrgLookupsDto> Handle(GetOrgLookups request, CancellationToken cancellationToken) => store.GetLookupsAsync(cancellationToken);
}
