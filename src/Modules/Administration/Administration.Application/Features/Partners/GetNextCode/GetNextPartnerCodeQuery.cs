using Administration.Application.Abstractions;
using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Partners.GetNextCode;

public sealed record GetNextPartnerCodeQuery : IQuery<string>;

public sealed class GetNextPartnerCodeQueryHandler(
    IPartnerReadRepository readRepo,
    ITenantContext tenant)
    : IQueryHandler<GetNextPartnerCodeQuery, string>
{
    public async Task<Result<string>> Handle(
        GetNextPartnerCodeQuery query,
        CancellationToken cancellationToken)
    {
        var code = await readRepo.GetNextCodeAsync(tenant.TenantId, cancellationToken);
        return Result.Success(code);
    }
}
