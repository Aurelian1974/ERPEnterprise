using Shared.Kernel.Abstractions;

namespace Administration.Application.Features.Users.GetCurrentUser;

public sealed record GetCurrentUserQuery(Guid UserId, Guid TenantId) : IQuery<CurrentUserDto>;

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
