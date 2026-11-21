using Erp.Core.Contracts;

namespace Erp.Core.Application.Organization;

/// <summary>
/// O localitate SIRUTA din lista formularelor de adresa (02.01, 3.5): numele oficial, tipul (Kind = TIP din SIRUTA), unitatea
/// administrativ-teritoriala careia ii apartine (comuna, orasul, municipiul) si codul postal.
/// </summary>
public sealed record CityOptionDto(int SirutaCode, string Name, byte Kind, string? ParentName, byte? ParentKind, string? PostalCode);

/// <summary>Cautarea in nomenclatorul SIRUTA (core.usp_City_Search, NET-06).</summary>
public interface ICityStore
{
    Task<IReadOnlyList<CityOptionDto>> SearchAsync(string countyCode, string? search, int? sirutaCode, CancellationToken ct);
}

/// <summary>Localitatile judetului care incep cu textul cautat (fara diacritice), cel mult 30; SirutaCode = si localitatea deja aleasa.</summary>
[Module("Core")]
[AuthenticatedUser]
public sealed record SearchCities(string County, string? Search = null, int? SirutaCode = null) : IQuery<IReadOnlyList<CityOptionDto>>;

internal sealed class SearchCitiesHandler(ICityStore store) : IQueryHandler<SearchCities, IReadOnlyList<CityOptionDto>>
{
    public Task<IReadOnlyList<CityOptionDto>> Handle(SearchCities request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.County))
        {
            return Task.FromResult<IReadOnlyList<CityOptionDto>>([]);
        }

        var search = string.IsNullOrWhiteSpace(request.Search) ? null
            : request.Search.Trim()[..Math.Min(request.Search.Trim().Length, 100)].Replace("[", "[[]", StringComparison.Ordinal)
                .Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);
        return store.SearchAsync(request.County.Trim().ToUpperInvariant(), search, request.SirutaCode, cancellationToken);
    }
}
