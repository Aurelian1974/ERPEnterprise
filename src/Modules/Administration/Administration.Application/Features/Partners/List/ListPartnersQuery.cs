using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.List;

public sealed record ListPartnersQuery(
    int Page = 1,
    int PageSize = 50,
    string? Search = null
) : IQuery<PagedResult<PartnerListItemDto>>;

public sealed class ListPartnersQueryHandler(
    IPartnerReadRepository readRepo,
    ITenantContext tenant)
    : IQueryHandler<ListPartnersQuery, PagedResult<PartnerListItemDto>>
{
    public async Task<Result<PagedResult<PartnerListItemDto>>> Handle(
        ListPartnersQuery request,
        CancellationToken cancellationToken)
    {
        var result = await readRepo.ListAsync(
            tenant.TenantId,
            request.Search,
            request.Page,
            request.PageSize,
            cancellationToken);

        return Result.Success(result);
    }
}
