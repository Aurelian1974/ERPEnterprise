namespace ValyanERP.Web.Features.Infrastructure.Security.Models;

/// <summary>
/// Represents a user's organizational access permission.
/// Maps to the UserOrganizationalAccess database table.
/// </summary>
public class UserOrganizationalAccess
{
    /// <summary>
    /// Unique identifier for this access record.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// The user who has this access.
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Type of entity: GROUP, COMPANY, WORKPLACE, LOCATION
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// The ID of the entity being accessed.
    /// </summary>
    public Guid EntityId { get; set; }
    
    /// <summary>
    /// Access level:
    /// 0 = NoAccess (explicit deny)
    /// 1 = Read (view only)
    /// 2 = Write (can create/edit)
    /// 3 = Full (can delete)
    /// 4 = Admin (can manage permissions)
    /// </summary>
    public byte AccessLevel { get; set; }
    
    /// <summary>
    /// If true, access is inherited to child entities.
    /// Example: GROUP access with InheritToChildren grants access to all companies in the group.
    /// </summary>
    public bool InheritToChildren { get; set; } = true;
    
    /// <summary>
    /// Optional start date for time-limited access.
    /// </summary>
    public DateTime? ValidFrom { get; set; }
    
    /// <summary>
    /// Optional end date for time-limited access.
    /// </summary>
    public DateTime? ValidTo { get; set; }
    
    /// <summary>
    /// Optional notes about this access grant.
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Whether this access is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When this access was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Who created this access.
    /// </summary>
    public Guid? CreatedBy { get; set; }
    
    /// <summary>
    /// When this access was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
    
    /// <summary>
    /// Who last updated this access.
    /// </summary>
    public Guid? UpdatedBy { get; set; }
    
    // =============================================
    // Navigation Properties (for display)
    // =============================================
    
    /// <summary>
    /// User's email (for display).
    /// </summary>
    public string? UserEmail { get; set; }
    
    /// <summary>
    /// User's full name (for display).
    /// </summary>
    public string? UserName { get; set; }
    
    /// <summary>
    /// Entity name (for display).
    /// </summary>
    public string? EntityName { get; set; }
    
    // =============================================
    // Helper Properties
    // =============================================
    
    /// <summary>
    /// Returns the access level as a display string.
    /// </summary>
    public string AccessLevelDisplay => AccessLevel switch
    {
        0 => "Fără acces",
        1 => "Citire",
        2 => "Scriere",
        3 => "Complet",
        4 => "Administrator",
        _ => "Necunoscut"
    };
    
    /// <summary>
    /// Returns the entity type as a display string.
    /// </summary>
    public string EntityTypeDisplay => EntityType.ToUpperInvariant() switch
    {
        "GROUP" => "Grup",
        "COMPANY" => "Societate",
        "WORKPLACE" => "Punct de Lucru",
        "LOCATION" => "Locație",
        _ => EntityType
    };
    
    /// <summary>
    /// Checks if the access is currently valid (within date range if specified).
    /// </summary>
    public bool IsCurrentlyValid
    {
        get
        {
            if (!IsActive)
                return false;
            
            var now = DateTime.Now;
            
            if (ValidFrom.HasValue && now < ValidFrom.Value)
                return false;
            
            if (ValidTo.HasValue && now > ValidTo.Value)
                return false;
            
            return true;
        }
    }
}

/// <summary>
/// Access level constants for organizational access.
/// </summary>
public static class AccessLevels
{
    public const byte NoAccess = 0;
    public const byte Read = 1;
    public const byte Write = 2;
    public const byte Full = 3;
    public const byte Admin = 4;
}
