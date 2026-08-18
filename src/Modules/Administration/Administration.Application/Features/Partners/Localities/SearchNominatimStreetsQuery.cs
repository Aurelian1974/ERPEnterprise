using Administration.Application.Abstractions;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.Localities;

public sealed record NominatimStreetDto(
    string DisplayName,
    string? StreetName,
    string? HouseNumber,
    string? City,
    string? County,
    string? PostalCode,
    string? Country,
    string? CountryCode,
    double? Lat,
    double? Lon,
    string? OsmType,
    long? OsmId);

public sealed record SearchNominatimStreetsQuery(
    string Country,
    string City,
    string Street,
    int Limit = 10) : IQuery<List<NominatimStreetDto>>;

public sealed class SearchNominatimStreetsQueryHandler(INominatimService service)
    : IQueryHandler<SearchNominatimStreetsQuery, List<NominatimStreetDto>>
{
    public async Task<Result<List<NominatimStreetDto>>> Handle(
        SearchNominatimStreetsQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.SearchStreetsAsync(
            request.Country.Trim(),
            request.City.Trim(),
            request.Street.Trim(),
            request.Limit,
            cancellationToken);

        if (!result.IsSuccess)
            return Result.Failure<List<NominatimStreetDto>>(result.Error);

        var dto = result.Value!.Select(x => new NominatimStreetDto(
            x.DisplayName,
            x.StreetName,
            x.HouseNumber,
            x.City,
            x.County,
            x.PostalCode,
            x.Country,
            x.CountryCode,
            x.Lat,
            x.Lon,
            x.OsmType,
            x.OsmId)).ToList();

        return Result.Success(dto);
    }
}
