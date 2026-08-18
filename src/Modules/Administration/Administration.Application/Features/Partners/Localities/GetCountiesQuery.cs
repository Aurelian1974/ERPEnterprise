using Administration.Application.Abstractions;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.Localities;

public sealed record CountyDto(string Code, string Name);

public sealed record GetCountiesQuery(string? CountryCode = null) : IQuery<List<CountyDto>>;

public sealed class GetCountiesQueryHandler(ILocalitatiService service)
    : IQueryHandler<GetCountiesQuery, List<CountyDto>>
{
    public async Task<Result<List<CountyDto>>> Handle(
        GetCountiesQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.CountryCode)
            && !string.Equals(request.CountryCode, "RO", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success<List<CountyDto>>([]);
        }

        var result = await service.GetCountiesAsync(cancellationToken);

        if (!result.IsSuccess)
            return Result.Failure<List<CountyDto>>(result.Error);

        return Result.Success(result.Value!.Select(c => new CountyDto(c.Code, c.Name)).ToList());
    }
}
