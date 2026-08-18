using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Primitives;

namespace Administration.Infrastructure.Services;

public sealed class NominatimService : INominatimService
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<NominatimService> _logger;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    public NominatimService(HttpClient http, IMemoryCache cache, ILogger<NominatimService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<List<NominatimStreetResult>>> SearchStreetsAsync(
        string country,
        string city,
        string street,
        int limit = 10,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(country)
            || string.IsNullOrWhiteSpace(city)
            || string.IsNullOrWhiteSpace(street))
        {
            return Result.Success<List<NominatimStreetResult>>([]);
        }

        var cacheKey = $"nominatim:streets:{country.ToLowerInvariant()}:{city.ToLowerInvariant()}:{street.ToLowerInvariant()}:{limit}";
        if (_cache.TryGetValue(cacheKey, out List<NominatimStreetResult>? cached) && cached is not null)
            return Result.Success(cached);

        var query = new StringBuilder();
        query.Append("street=").Append(Uri.EscapeDataString(street.Trim()));
        query.Append("&city=").Append(Uri.EscapeDataString(city.Trim()));
        query.Append("&country=").Append(Uri.EscapeDataString(country.Trim()));
        query.Append("&format=jsonv2");
        query.Append("&addressdetails=1");
        query.Append("&dedupe=1");
        query.Append("&limit=").Append(Math.Clamp(limit, 1, 40).ToString(CultureInfo.InvariantCulture));

        var url = $"search?{query}";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nominatim search request failed");
            return Result.Failure<List<NominatimStreetResult>>(AdministrationErrors.NominatimServiceUnavailable());
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Nominatim search failed: {StatusCode}", response.StatusCode);
            return Result.Failure<List<NominatimStreetResult>>(AdministrationErrors.NominatimServiceUnavailable());
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var items = JsonSerializer.Deserialize<List<NominatimSearchItem>>(json, JsonOptions);

        var results = items?
            .Select(MapItem)
            .Where(r => !string.IsNullOrWhiteSpace(r.DisplayName))
            .ToList() ?? [];

        _cache.Set(cacheKey, results, CacheDuration);
        return Result.Success(results);
    }

    private static NominatimStreetResult MapItem(NominatimSearchItem item)
    {
        var address = item.Address ?? new NominatimAddress();
        var streetName = address.Road
            ?? address.Pedestrian
            ?? address.Path
            ?? address.Footway
            ?? address.Street
            ?? item.Name;

        return new NominatimStreetResult(
            item.DisplayName ?? string.Empty,
            streetName,
            address.HouseNumber,
            address.City ?? address.Town ?? address.Village ?? address.Suburb,
            address.County ?? address.StateDistrict ?? address.State,
            address.Postcode,
            address.Country,
            address.CountryCode,
            string.IsNullOrWhiteSpace(item.Lat)
                ? null
                : double.Parse(item.Lat, CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(item.Lon)
                ? null
                : double.Parse(item.Lon, CultureInfo.InvariantCulture),
            item.OsmType,
            item.OsmId == 0 ? null : item.OsmId);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record NominatimSearchItem(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("lat")] string? Lat,
        [property: JsonPropertyName("lon")] string? Lon,
        [property: JsonPropertyName("osm_type")] string? OsmType,
        [property: JsonPropertyName("osm_id")] long OsmId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("address")] NominatimAddress? Address);

    private sealed class NominatimAddress
    {
        [JsonPropertyName("road")] public string? Road { get; init; }
        [JsonPropertyName("pedestrian")] public string? Pedestrian { get; init; }
        [JsonPropertyName("path")] public string? Path { get; init; }
        [JsonPropertyName("footway")] public string? Footway { get; init; }
        [JsonPropertyName("street")] public string? Street { get; init; }
        [JsonPropertyName("house_number")] public string? HouseNumber { get; init; }
        [JsonPropertyName("city")] public string? City { get; init; }
        [JsonPropertyName("town")] public string? Town { get; init; }
        [JsonPropertyName("village")] public string? Village { get; init; }
        [JsonPropertyName("suburb")] public string? Suburb { get; init; }
        [JsonPropertyName("county")] public string? County { get; init; }
        [JsonPropertyName("state_district")] public string? StateDistrict { get; init; }
        [JsonPropertyName("state")] public string? State { get; init; }
        [JsonPropertyName("postcode")] public string? Postcode { get; init; }
        [JsonPropertyName("country")] public string? Country { get; init; }
        [JsonPropertyName("country_code")] public string? CountryCode { get; init; }
    }
}
