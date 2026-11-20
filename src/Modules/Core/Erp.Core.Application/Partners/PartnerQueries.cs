using Erp.Core.Contracts;

namespace Erp.Core.Application.Partners;

/// <summary>
/// Lista partenerilor (macheta 04): cautare dupa inceputul denumirii (fara majuscule si diacritice), al codului sau al CUI-ului
/// („RO” optional); Role: 1 clienti, 2 furnizori. Filtrul pe proprietate cere PropertyDefinitionId si optiunea sau valoarea da/nu.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerView)]
public sealed record SearchPartners(string? Search = null, byte? Role = null, byte? PartnerType = null, bool? IsActive = null, bool? AnafInactive = null,
    int? PropertyDefinitionId = null, int? PropertyOptionId = null, bool? PropertyBool = null, int Page = 1, int PageSize = 50) : IQuery<PartnerPageDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.PartnerView)]
public sealed record GetPartner(int Id) : IQuery<PartnerDetailDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.PartnerView)]
public sealed record GetPartnerLookups : IQuery<PartnerLookupsDto>;

/// <summary>Tab-ul Istoric al fisei: auditul partenerului si al adreselor, conturilor si contactelor lui.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerView)]
public sealed record GetPartnerHistory(int Id) : IQuery<IReadOnlyList<PartnerHistoryEntryDto>>;

internal sealed class SearchPartnersHandler(IPartnerStore store) : IQueryHandler<SearchPartners, PartnerPageDto>
{
    public Task<PartnerPageDto> Handle(SearchPartners request, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : Escape(request.Search.Trim());
        var digits = search is null ? null : Domain.RomanianFiscalCode.Normalize(search) is { } cui ? cui : null;
        var fiscal = digits ?? (search is { Length: >= 3 } ? search.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant() : null);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(request.Page, 1);
        return store.SearchAsync(new PartnerSearchCriteria(search, fiscal, request.Role is 1 or 2 ? request.Role : null, request.PartnerType,
            request.IsActive, request.AnafInactive, request.PropertyDefinitionId, request.PropertyOptionId, request.PropertyBool,
            (page - 1) * pageSize, pageSize), cancellationToken);
    }

    // Textul cautat ajunge intr-un LIKE: caracterele speciale se cauta literal.
    private static string Escape(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);
}

internal sealed class GetPartnerHandler(IPartnerStore store) : IQueryHandler<GetPartner, PartnerDetailDto>
{
    public async Task<PartnerDetailDto> Handle(GetPartner request, CancellationToken cancellationToken) =>
        await store.GetAsync(request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Partenerul nu există.");
}

internal sealed class GetPartnerLookupsHandler(IPartnerStore store, ICurrentContext context) : IQueryHandler<GetPartnerLookups, PartnerLookupsDto>
{
    public Task<PartnerLookupsDto> Handle(GetPartnerLookups request, CancellationToken cancellationToken) =>
        store.GetLookupsAsync(context.CompanyId, cancellationToken);
}

internal sealed class GetPartnerHistoryHandler(IPartnerStore store) : IQueryHandler<GetPartnerHistory, IReadOnlyList<PartnerHistoryEntryDto>>
{
    public Task<IReadOnlyList<PartnerHistoryEntryDto>> Handle(GetPartnerHistory request, CancellationToken cancellationToken) =>
        store.GetHistoryAsync(request.Id, cancellationToken);
}
