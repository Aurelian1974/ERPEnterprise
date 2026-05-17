using Shared.Kernel.Abstractions;
using Shared.Kernel.Primitives;

namespace Administration.Application.Features.Users.GetCurrentUser;

public sealed class GetCurrentUserQueryHandler : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public Task<Result<CurrentUserDto>> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        // TODO: implement user lookup via repository
        throw new NotImplementedException();
    }
}
