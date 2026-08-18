using Shared.Kernel.Primitives;

namespace Administration.Application.Abstractions;

public sealed record NominatimStreetResult(
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

public interface INominatimService
{
    Task<Result<List<NominatimStreetResult>>> SearchStreetsAsync(
        string country,
        string city,
        string street,
        int limit = 10,
        CancellationToken ct = default);
}
