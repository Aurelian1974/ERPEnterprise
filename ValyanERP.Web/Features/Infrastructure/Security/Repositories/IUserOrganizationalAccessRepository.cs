// ========================================================================
// IUserOrganizationalAccessRepository.cs
// Repository interface for UserOrganizationalAccess CRUD operations
// Features/Infrastructure/Security/Repositories
// ========================================================================

using ValyanERP.Web.Features.Infrastructure.Security.Models;
using ValyanERP.Web.Features.Infrastructure.Security.Models.DTOs;

namespace ValyanERP.Web.Features.Infrastructure.Security.Repositories;

/// <summary>
/// Repository interface for managing user organizational access permissions.
/// </summary>
public interface IUserOrganizationalAccessRepository
{
    #region CRUD Operations
    
    /// <summary>
    /// Gets all access records for a specific user.
    /// </summary>
    Task<IEnumerable<UserAccessListDto>> GetByUserIdAsync(Guid userId);
    
    /// <summary>
    /// Gets a single access record by ID.
    /// </summary>
    Task<UserOrganizationalAccess?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Creates a new access permission.
    /// </summary>
    Task<Guid> CreateAsync(CreateUserAccessDto dto, Guid createdBy);
    
    /// <summary>
    /// Updates an existing access permission.
    /// </summary>
    Task<bool> UpdateAsync(UpdateUserAccessDto dto, Guid updatedBy);
    
    /// <summary>
    /// Revokes (soft deletes) an access permission.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, Guid deletedBy);
    
    #endregion
    
    #region Bulk Operations
    
    /// <summary>
    /// Creates multiple access permissions at once.
    /// </summary>
    Task<int> BulkCreateAsync(IEnumerable<CreateUserAccessDto> dtos, Guid createdBy);
    
    /// <summary>
    /// Copies all access permissions from one user to another.
    /// </summary>
    Task<int> CopyFromUserAsync(Guid sourceUserId, Guid targetUserId, Guid createdBy);
    
    /// <summary>
    /// Revokes all access for a user.
    /// </summary>
    Task<int> RevokeAllForUserAsync(Guid userId, Guid revokedBy);
    
    #endregion
    
    #region Query Operations
    
    /// <summary>
    /// Gets the organizational tree with user access information.
    /// </summary>
    Task<IEnumerable<OrganizationTreeNode>> GetOrganizationTreeAsync(Guid? userId = null);
    
    /// <summary>
    /// Gets all users who have access to a specific entity.
    /// </summary>
    Task<IEnumerable<UserAccessListDto>> GetUsersWithAccessAsync(string entityType, Guid entityId);
    
    /// <summary>
    /// Gets a summary of user's perimeter.
    /// </summary>
    Task<UserPerimeterSummaryDto?> GetUserPerimeterSummaryAsync(Guid userId);
    
    /// <summary>
    /// Checks if a user has access to a specific entity.
    /// </summary>
    Task<(bool HasAccess, byte AccessLevel)> CheckAccessAsync(Guid userId, string entityType, Guid entityId);
    
    #endregion
    
    #region Cache Operations
    
    /// <summary>
    /// Refreshes the visibility cache for a user.
    /// </summary>
    Task RefreshUserCacheAsync(Guid userId);
    
    #endregion
}
