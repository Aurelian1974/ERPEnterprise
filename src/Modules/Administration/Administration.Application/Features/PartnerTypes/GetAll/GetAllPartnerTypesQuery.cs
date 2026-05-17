using Administration.Domain.Partners;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.PartnerTypes.GetAll;

public sealed record GetAllPartnerTypesQuery(bool IncludeInactive = false)
    : IQuery<IReadOnlyList<PartnerTypeDto>>;

public sealed class GetAllPartnerTypesQueryHandler(IPartnerTypeRepository repository)
    : IQueryHandler<GetAllPartnerTypesQuery, IReadOnlyList<PartnerTypeDto>>
{
    public async Task<Result<IReadOnlyList<PartnerTypeDto>>> Handle(
        GetAllPartnerTypesQuery request,
        CancellationToken cancellationToken)
    {
        var types = await repository.GetAllAsync(request.IncludeInactive, cancellationToken);
        IReadOnlyList<PartnerTypeDto> dtos = types.Select(PartnerTypeDto.FromDomain).ToList();
        return Result.Success(dtos);
    }
}
