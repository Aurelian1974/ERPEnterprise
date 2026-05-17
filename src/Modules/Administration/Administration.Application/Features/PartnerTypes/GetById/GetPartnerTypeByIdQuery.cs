using Administration.Domain.Partners;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.PartnerTypes.GetById;

public sealed record GetPartnerTypeByIdQuery(byte Id) : IQuery<PartnerTypeDto>;

public sealed class GetPartnerTypeByIdQueryHandler(IPartnerTypeRepository repository)
    : IQueryHandler<GetPartnerTypeByIdQuery, PartnerTypeDto>
{
    public async Task<Result<PartnerTypeDto>> Handle(
        GetPartnerTypeByIdQuery request,
        CancellationToken cancellationToken)
    {
        var partnerType = await repository.GetByIdAsync(request.Id, cancellationToken);

        if (partnerType is null)
            return Result.Failure<PartnerTypeDto>(AdministrationErrors.PartnerTypeNotFound(request.Id));

        return Result.Success(PartnerTypeDto.FromDomain(partnerType));
    }
}
