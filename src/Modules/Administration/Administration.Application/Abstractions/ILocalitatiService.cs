using Shared.Kernel.Primitives;

namespace Administration.Application.Abstractions;

public sealed record LocalityCounty(
    string Code,
    string Name);

public sealed record LocalitySearchResult(
    string Name,
    LocalityCounty County,
    string? Type,
    long? Siruta,
    string? PostalCode);

public sealed record LocalityValidationMatch(
    string Name,
    LocalityCounty County,
    string? Type,
    long? Siruta,
    string? PostalCode);

public sealed record LocalityValidationResult(
    bool Valid,
    double Confidence,
    LocalityValidationMatch? Match);

public interface ILocalitatiService
{
    Task<Result<List<LocalitySearchResult>>> SearchAsync(
        string query,
        string? county = null,
        int limit = 10,
        CancellationToken ct = default);

    Task<Result<List<LocalityCounty>>> GetCountiesAsync(CancellationToken ct = default);

    Task<Result<LocalityValidationResult>> ValidateAsync(
        string name,
        string? county = null,
        CancellationToken ct = default);
}
