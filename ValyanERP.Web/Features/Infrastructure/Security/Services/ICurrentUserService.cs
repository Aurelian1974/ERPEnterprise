namespace ValyanERP.Web.Features.Infrastructure.Security.Services;

/// <summary>
/// Interface for accessing current authenticated user information.
/// Provides user identity details and role verification.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Gets the current user's ID (GUID).
    /// </summary>
    Guid UserId { get; }
    
    /// <summary>
    /// Gets the current user's email address.
    /// </summary>
    string? Email { get; }
    
    /// <summary>
    /// Gets whether the current user is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }
    
    /// <summary>
    /// Gets the current user's ID or null if not authenticated.
    /// </summary>
    Guid? GetUserIdOrNull();
    
    /// <summary>
    /// Checks if the current user is in a specific role.
    /// </summary>
    /// <param name="role">The role name to check.</param>
    /// <returns>True if user is in the specified role.</returns>
    Task<bool> IsInRoleAsync(string role);
    
    /// <summary>
    /// Gets all roles for the current user.
    /// </summary>
    /// <returns>Collection of role names.</returns>
    Task<IEnumerable<string>> GetRolesAsync();
}
