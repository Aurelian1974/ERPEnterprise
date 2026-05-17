using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.GetById;

public sealed record GetPartnerByIdQuery(Guid Id) : IQuery<PartnerDetailDto>;

public sealed class GetPartnerByIdQueryHandler(
    IPartnerReadRepository readRepo,
    ITenantContext tenant)
    : IQueryHandler<GetPartnerByIdQuery, PartnerDetailDto>
{
    public async Task<Result<PartnerDetailDto>> Handle(
        GetPartnerByIdQuery request,
        CancellationToken cancellationToken)
    {
        var dto = await readRepo.GetByIdAsync(request.Id, tenant.TenantId, cancellationToken);

        if (dto is null)
            return Result.Failure<PartnerDetailDto>(AdministrationErrors.PartnerNotFound(request.Id));

        return Result.Success(dto);
    }
}
