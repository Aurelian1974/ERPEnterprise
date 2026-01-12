namespace ValyanERP.Web.Features.Infrastructure.Security.Models;

/// <summary>
/// Represents the organizational perimeter (visibility scope) for a user.
/// Contains all entities the user can access based on their organizational permissions.
/// Uses HashSet for O(1) lookups during access checks.
/// </summary>
public class UserPerimeter
{
    /// <summary>
    /// The user ID this perimeter belongs to.
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// If true, user has admin access and can see all entities.
    /// </summary>
    public bool HasFullAccess { get; set; }
    
    /// <summary>
    /// Set of visible Group IDs.
    /// </summary>
    public HashSet<Guid> VisibleGroupIds { get; set; } = new();
    
    /// <summary>
    /// Set of visible Company IDs.
    /// </summary>
    public HashSet<Guid> VisibleCompanyIds { get; set; } = new();
    
    /// <summary>
    /// Set of visible WorkPlace IDs.
    /// </summary>
    public HashSet<Guid> VisibleWorkPlaceIds { get; set; } = new();
    
    /// <summary>
    /// Set of visible Location IDs.
    /// </summary>
    public HashSet<Guid> VisibleLocationIds { get; set; } = new();
    
    /// <summary>
    /// Access levels per company (for granular write permission checks).
    /// Key: CompanyId, Value: AccessLevel (0=None, 1=Read, 2=Write, 3=Full, 4=Admin)
    /// </summary>
    public Dictionary<Guid, byte> CompanyAccessLevels { get; set; } = new();
    
    /// <summary>
    /// Access levels per location.
    /// Key: LocationId, Value: AccessLevel
    /// </summary>
    public Dictionary<Guid, byte> LocationAccessLevels { get; set; } = new();
    
    /// <summary>
    /// Access levels per workplace.
    /// Key: WorkPlaceId, Value: AccessLevel
    /// </summary>
    public Dictionary<Guid, byte> WorkPlaceAccessLevels { get; set; } = new();
    
    /// <summary>
    /// Access levels per group.
    /// Key: GroupId, Value: AccessLevel
    /// </summary>
    public Dictionary<Guid, byte> GroupAccessLevels { get; set; } = new();
    
    /// <summary>
    /// When this perimeter was cached.
    /// </summary>
    public DateTime CachedAt { get; set; }
    
    // =============================================
    // Helper Methods for Access Checks
    // =============================================
    
    /// <summary>
    /// Checks if user can access a specific company (Read level).
    /// </summary>
    public bool CanAccessCompany(Guid companyId) => 
        HasFullAccess || VisibleCompanyIds.Contains(companyId);
    
    /// <summary>
    /// Checks if user can access a specific location (Read level).
    /// </summary>
    public bool CanAccessLocation(Guid locationId) => 
        HasFullAccess || VisibleLocationIds.Contains(locationId);
    
    /// <summary>
    /// Checks if user can access a specific workplace (Read level).
    /// </summary>
    public bool CanAccessWorkPlace(Guid workPlaceId) => 
        HasFullAccess || VisibleWorkPlaceIds.Contains(workPlaceId);
    
    /// <summary>
    /// Checks if user can access a specific group (Read level).
    /// </summary>
    public bool CanAccessGroup(Guid groupId) => 
        HasFullAccess || VisibleGroupIds.Contains(groupId);
    
    /// <summary>
    /// Checks if user can write to a specific company (Write level or higher).
    /// </summary>
    public bool CanWriteToCompany(Guid companyId) => 
        HasFullAccess || (CompanyAccessLevels.TryGetValue(companyId, out var level) && level >= 2);
    
    /// <summary>
    /// Checks if user can write to a specific location (Write level or higher).
    /// </summary>
    public bool CanWriteToLocation(Guid locationId) => 
        HasFullAccess || (LocationAccessLevels.TryGetValue(locationId, out var level) && level >= 2);
    
    /// <summary>
    /// Checks if user has full access to a company (Full level or Admin).
    /// </summary>
    public bool HasFullAccessToCompany(Guid companyId) => 
        HasFullAccess || (CompanyAccessLevels.TryGetValue(companyId, out var level) && level >= 3);
    
    /// <summary>
    /// Checks if user is admin for a specific company.
    /// </summary>
    public bool IsAdminForCompany(Guid companyId) => 
        HasFullAccess || (CompanyAccessLevels.TryGetValue(companyId, out var level) && level >= 4);
    
    /// <summary>
    /// Gets the access level for a specific entity.
    /// </summary>
    /// <param name="entityType">Type: GROUP, COMPANY, WORKPLACE, LOCATION</param>
    /// <param name="entityId">Entity ID</param>
    /// <returns>Access level (0-4) or 0 if no access</returns>
    public byte GetAccessLevel(string entityType, Guid entityId)
    {
        if (HasFullAccess)
            return 4; // Admin
        
        return entityType.ToUpperInvariant() switch
        {
            "GROUP" => GroupAccessLevels.TryGetValue(entityId, out var gl) ? gl : (byte)0,
            "COMPANY" => CompanyAccessLevels.TryGetValue(entityId, out var cl) ? cl : (byte)0,
            "WORKPLACE" => WorkPlaceAccessLevels.TryGetValue(entityId, out var wl) ? wl : (byte)0,
            "LOCATION" => LocationAccessLevels.TryGetValue(entityId, out var ll) ? ll : (byte)0,
            _ => 0
        };
    }
    
    /// <summary>
    /// Checks if perimeter is expired (older than 30 minutes).
    /// </summary>
    public bool IsExpired => (DateTime.UtcNow - CachedAt).TotalMinutes > 30;
}
