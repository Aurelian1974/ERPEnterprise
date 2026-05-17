namespace Shared.Kernel.Abstractions;

public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    bool IsAuthenticated { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasPermission(string permission);
}
