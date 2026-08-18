using Administration.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Shared.Kernel.Primitives;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Administration.Infrastructure.Services;

public sealed class LocalitatiService : ILocalitatiService
{
    private readonly HttpClient _http;
    private readonly ILogger<LocalitatiService> _logger;

    public LocalitatiService(HttpClient http, ILogger<LocalitatiService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<Result<List<LocalitySearchResult>>> SearchAsync(
        string query,
        string? county = null,
        int limit = 10,
        CancellationToken ct = default)
    {
        var url = $"search?q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(county))
            url += $"&county={Uri.EscapeDataString(county)}";
        url += $"&limit={limit}";

        var response = await GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("localitati.dev search failed: {StatusCode} for query '{Query}'", response.StatusCode, query);
            return Result.Failure<List<LocalitySearchResult>>(
                Administration.Application.Features.PartnerTypes.AdministrationErrors.LocalitatiServiceUnavailable());
        }

        var data = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken: ct);
        var results = data?.Results?.Select(MapSearchResult).ToList() ?? [];
        return Result.Success(results);
    }

    public async Task<Result<List<LocalityCounty>>> GetCountiesAsync(CancellationToken ct = default)
    {
        var response = await GetAsync("counties", ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("localitati.dev counties failed: {StatusCode}", response.StatusCode);
            return Result.Failure<List<LocalityCounty>>(
                Administration.Application.Features.PartnerTypes.AdministrationErrors.LocalitatiServiceUnavailable());
        }

        var data = await response.Content.ReadFromJsonAsync<CountiesResponse>(cancellationToken: ct);
        var results = data?.Counties?.Select(c => new LocalityCounty(c.Code, c.Name)).ToList() ?? [];
        return Result.Success(results);
    }

    public async Task<Result<LocalityValidationResult>> ValidateAsync(
        string name,
        string? county = null,
        CancellationToken ct = default)
    {
        var url = $"validate?name={Uri.EscapeDataString(name)}";
        if (!string.IsNullOrWhiteSpace(county))
            url += $"&county={Uri.EscapeDataString(county)}";

        var response = await GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("localitati.dev validate failed: {StatusCode} for name '{Name}'", response.StatusCode, name);
            return Result.Failure<LocalityValidationResult>(
                Administration.Application.Features.PartnerTypes.AdministrationErrors.LocalitatiServiceUnavailable());
        }

        var data = await response.Content.ReadFromJsonAsync<ValidateResponse>(cancellationToken: ct);
        if (data is null)
        {
            return Result.Success(new LocalityValidationResult(false, 0, null));
        }

        var match = data.Match is null
            ? null
            : new LocalityValidationMatch(
                data.Match.Name,
                new LocalityCounty(data.Match.County.Code, data.Match.County.Name),
                data.Match.Type,
                data.Match.Siruta,
                data.Match.PostalCode);

        return Result.Success(new LocalityValidationResult(data.Valid, data.Confidence, match));
    }

    private async Task<HttpResponseMessage> GetAsync(string relativeUrl, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        return await _http.GetAsync(relativeUrl, cts.Token);
    }

    private static LocalitySearchResult MapSearchResult(SearchResultItem item)
    {
        return new LocalitySearchResult(
            item.Name,
            new LocalityCounty(item.County.Code, item.County.Name),
            item.Type,
            item.Siruta,
            item.PostalCode);
    }

    // ─── Raw JSON models ─────────────────────────────────────────────────────

    private sealed record SearchResponse(
        [property: JsonPropertyName("results")] List<SearchResultItem>? Results);

    private sealed record SearchResultItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("county")] CountyItem County,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("siruta")] long? Siruta,
        [property: JsonPropertyName("postal_code")] string? PostalCode);

    private sealed record CountiesResponse(
        [property: JsonPropertyName("results")] List<CountyItem>? Counties);

    private sealed record CountyItem(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("name")] string Name);

    private sealed record ValidateResponse(
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("match")] ValidateMatchItem? Match);

    private sealed record ValidateMatchItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("county")] CountyItem County,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("siruta")] long? Siruta,
        [property: JsonPropertyName("postal_code")] string? PostalCode);
}
