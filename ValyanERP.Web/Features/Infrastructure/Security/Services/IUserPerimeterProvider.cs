using ValyanERP.Web.Features.Infrastructure.Security.Models;

namespace ValyanERP.Web.Features.Infrastructure.Security.Services;

/// <summary>
/// Provides the organizational perimeter (visibility scope) for the current user.
/// Manages caching and invalidation of user permissions.
/// </summary>
public interface IUserPerimeterProvider
{
    /// <summary>
    /// Gets the complete perimeter for the current user.
    /// Results are cached for performance.
    /// </summary>
    /// <returns>The user's organizational perimeter.</returns>
    Task<UserPerimeter> GetPerimeterAsync();
    
    /// <summary>
    /// Gets all visible company IDs for the current user.
    /// If user is admin, returns all active companies.
    /// </summary>
    /// <returns>Collection of visible company IDs.</returns>
    Task<IEnumerable<Guid>> GetVisibleCompanyIdsAsync();
    
    /// <summary>
    /// Gets all visible location IDs for the current user.
    /// If user is admin, returns all active locations.
    /// </summary>
    /// <returns>Collection of visible location IDs.</returns>
    Task<IEnumerable<Guid>> GetVisibleLocationIdsAsync();
    
    /// <summary>
    /// Gets all visible workplace IDs for the current user.
    /// </summary>
    /// <returns>Collection of visible workplace IDs.</returns>
    Task<IEnumerable<Guid>> GetVisibleWorkPlaceIdsAsync();
    
    /// <summary>
    /// Gets all visible group IDs for the current user.
    /// </summary>
    /// <returns>Collection of visible group IDs.</returns>
    Task<IEnumerable<Guid>> GetVisibleGroupIdsAsync();
    
    /// <summary>
    /// Checks if the current user can access a specific entity.
    /// </summary>
    /// <param name="entityType">Type: GROUP, COMPANY, WORKPLACE, LOCATION</param>
    /// <param name="entityId">Entity ID to check.</param>
    /// <param name="requiredLevel">Minimum required access level (default: 1 = Read).</param>
    /// <returns>True if user has sufficient access.</returns>
    Task<bool> CanAccessAsync(string entityType, Guid entityId, byte requiredLevel = 1);
    
    /// <summary>
    /// Checks if the current user can write to a specific company.
    /// </summary>
    /// <param name="companyId">Company ID to check.</param>
    /// <returns>True if user has write access.</returns>
    Task<bool> CanWriteToCompanyAsync(Guid companyId);
    
    /// <summary>
    /// Checks if the current user can write to a specific location.
    /// </summary>
    /// <param name="locationId">Location ID to check.</param>
    /// <returns>True if user has write access.</returns>
    Task<bool> CanWriteToLocationAsync(Guid locationId);
    
    /// <summary>
    /// Invalidates the cached perimeter for the current user.
    /// Call this when user permissions change.
    /// </summary>
    Task InvalidateCacheAsync();
    
    /// <summary>
    /// Invalidates the cached perimeter for a specific user.
    /// Use for admin operations that modify another user's permissions.
    /// </summary>
    /// <param name="userId">User ID to invalidate.</param>
    Task InvalidateCacheForUserAsync(Guid userId);
}
