using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.Localities;

public sealed record LocalityDto(
    string Name,
    string CountyCode,
    string CountyName,
    string? Type,
    long? Siruta,
    string? PostalCode);

public sealed record SearchLocalitiesQuery(
    string Query,
    string? County = null,
    string? CountryCode = null,
    int Limit = 10) : IQuery<List<LocalityDto>>;

public sealed class SearchLocalitiesQueryHandler(ILocalitatiService service)
    : IQueryHandler<SearchLocalitiesQuery, List<LocalityDto>>
{
    public async Task<Result<List<LocalityDto>>> Handle(
        SearchLocalitiesQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return Result.Success<List<LocalityDto>>([]);

        if (!string.IsNullOrWhiteSpace(request.CountryCode)
            && !string.Equals(request.CountryCode, "RO", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success<List<LocalityDto>>([]);
        }

        var result = await service.SearchAsync(
            request.Query.Trim(),
            request.County,
            request.Limit,
            cancellationToken);

        if (!result.IsSuccess)
            return Result.Failure<List<LocalityDto>>(result.Error);

        var dto = result.Value!.Select(x => new LocalityDto(
            x.Name,
            x.County.Code,
            x.County.Name,
            x.Type,
            x.Siruta,
            x.PostalCode)).ToList();

        return Result.Success(dto);
    }
}
