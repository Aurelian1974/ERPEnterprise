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
    public Task<PartnerPageDto> Handle(SearchPartners request, CancellationToken cancellationToken) =>
        store.SearchAsync(Criteria(request), cancellationToken);

    /// <summary>Filtrele cererii in forma procedurii; folosite si de exportul Excel, ca exportul sa contina exact lista filtrata.</summary>
    internal static PartnerSearchCriteria Criteria(SearchPartners request)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : Escape(request.Search.Trim());
        var digits = search is null ? null : Domain.RomanianFiscalCode.Normalize(search) is { } cui ? cui : null;
        var fiscal = digits ?? (search is { Length: >= 3 } ? search.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant() : null);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(request.Page, 1);
        return new PartnerSearchCriteria(search, fiscal, request.Role is 1 or 2 ? request.Role : null, request.PartnerType,
            request.IsActive, request.AnafInactive, request.PropertyDefinitionId, request.PropertyOptionId, request.PropertyBool,
            (page - 1) * pageSize, pageSize);
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

/// <summary>Exportul Excel al listei (macheta 04, „Export Excel”; 02.01, 10): aceleasi filtre, coloanele sablonului de import.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.PartnerView)]
public sealed record ExportPartners(string? Search = null, byte? Role = null, byte? PartnerType = null, bool? IsActive = null, bool? AnafInactive = null,
    int? PropertyDefinitionId = null, int? PropertyOptionId = null, bool? PropertyBool = null) : IQuery<FileDto>;

/// <summary>Sablonul Excel de import parteneri (02.01, 4.8 pasul 1): antetul, un rand exemplu si listele de valori.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
public sealed record GetPartnerImportTemplate : IQuery<FileDto>;

internal sealed class ExportPartnersHandler(IPartnerStore store, IPartnerSpreadsheet spreadsheet, PartnerSheetListsProvider lists, TimeProvider clock)
    : IQueryHandler<ExportPartners, FileDto>
{
    public async Task<FileDto> Handle(ExportPartners request, CancellationToken cancellationToken)
    {
        var criteria = SearchPartnersHandler.Criteria(new SearchPartners(request.Search, request.Role, request.PartnerType, request.IsActive,
            request.AnafInactive, request.PropertyDefinitionId, request.PropertyOptionId, request.PropertyBool));
        var rows = await store.ExportAsync(criteria, cancellationToken).ConfigureAwait(false);
        var values = rows.Select(r => (IReadOnlyList<string?>)
        [
            r.Code, r.Name, PartnerSheet.TypeName(r.PartnerType), PartnerSheet.YesNoText(r.IsCustomer), PartnerSheet.YesNoText(r.IsSupplier),
            r.PartnerType == (byte)Domain.PartnerType.LegalRomania && r.VatPayer && r.FiscalCode is not null ? "RO" + r.FiscalCode : r.FiscalCode,
            r.TradeRegisterNo, PartnerSheet.YesNoText(r.VatPayer), PartnerSheet.YesNoText(r.VatOnCollection), PartnerSheet.YesNoText(r.AnafInactive),
            r.GroupCode, r.Address, r.City, r.CountyCode, r.CountryCode, r.PostalCode, r.Phone, r.Email, PartnerSheet.YesNoText(r.IsActive),
        ]).ToList();
        var content = spreadsheet.Write(values, await lists.GetAsync(cancellationToken).ConfigureAwait(false));
        var stamp = clock.GetLocalNow().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return new FileDto($"parteneri-{stamp}.xlsx", FileDto.Xlsx, content);
    }
}

internal sealed class GetPartnerImportTemplateHandler(IPartnerSpreadsheet spreadsheet, PartnerSheetListsProvider lists)
    : IQueryHandler<GetPartnerImportTemplate, FileDto>
{
    public async Task<FileDto> Handle(GetPartnerImportTemplate request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string?> example = [.. PartnerSheet.Columns.Select(c => (string?)(c.Example.Length == 0 ? null : c.Example))];
        var content = spreadsheet.Write([example], await lists.GetAsync(cancellationToken).ConfigureAwait(false));
        return new FileDto("sablon-import-parteneri.xlsx", FileDto.Xlsx, content);
    }
}

/// <summary>Listele foii „Liste”: codurile grupelor active, codurile judetelor romanesti si ale tarilor.</summary>
internal sealed class PartnerSheetListsProvider(IPartnerStore partners, Organization.IOrganizationStore organization, ICurrentContext context)
{
    public async Task<PartnerSheetLists> GetAsync(CancellationToken ct)
    {
        var partnerLookups = await partners.GetLookupsAsync(context.CompanyId, ct).ConfigureAwait(false);
        var orgLookups = await organization.GetLookupsAsync(ct).ConfigureAwait(false);
        return new PartnerSheetLists(
            [.. partnerLookups.Groups.Where(g => g.IsActive).Select(g => g.Code)],
            [.. orgLookups.Counties.Where(c => c.CountryCode == "RO").Select(c => c.Code)],
            [.. orgLookups.Countries.Select(c => c.Code)]);
    }
}
