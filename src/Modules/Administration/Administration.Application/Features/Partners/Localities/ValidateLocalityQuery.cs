using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.Localities;

public sealed record LocalityValidationDto(
    bool Valid,
    double Confidence,
    LocalityDto? Match);

public sealed record ValidateLocalityQuery(
    string Name,
    string? County = null,
    string? CountryCode = null) : IQuery<LocalityValidationDto>;

public sealed class ValidateLocalityQueryHandler(ILocalitatiService service)
    : IQueryHandler<ValidateLocalityQuery, LocalityValidationDto>
{
    public async Task<Result<LocalityValidationDto>> Handle(
        ValidateLocalityQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Success(new LocalityValidationDto(false, 0, null));
        }

        if (!string.IsNullOrWhiteSpace(request.CountryCode)
            && !string.Equals(request.CountryCode, "RO", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(new LocalityValidationDto(true, 1, null));
        }

        var result = await service.ValidateAsync(
            request.Name.Trim(),
            request.County,
            cancellationToken);

        if (!result.IsSuccess)
            return Result.Failure<LocalityValidationDto>(result.Error);

        var value = result.Value!;

        if (!value.Valid)
        {
            return Result.Failure<LocalityValidationDto>(
                AdministrationErrors.LocalityValidationFailed(request.Name, request.County));
        }
        var match = value.Match is null
            ? null
            : new LocalityDto(
                value.Match.Name,
                value.Match.County.Code,
                value.Match.County.Name,
                value.Match.Type,
                value.Match.Siruta,
                value.Match.PostalCode);

        return Result.Success(new LocalityValidationDto(value.Valid, value.Confidence, match));
    }
}
