// ========================================================================
// DTOs for UserOrganizationalAccess CRUD operations
// Features/Infrastructure/Security/Models/DTOs
// ========================================================================

using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Infrastructure.Security.Models.DTOs;

/// <summary>
/// DTO for displaying user access in grid/list.
/// </summary>
public class UserAccessListDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public byte AccessLevel { get; set; }
    public string AccessLevelName { get; set; } = string.Empty;
    public bool InheritToChildren { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO for creating a new access permission.
/// </summary>
public class CreateUserAccessDto
{
    [Required]
    public Guid UserId { get; set; }
    
    [Required]
    [StringLength(20)]
    public string EntityType { get; set; } = string.Empty;
    
    [Required]
    public Guid EntityId { get; set; }
    
    [Range(0, 4)]
    public byte AccessLevel { get; set; } = 1;
    
    public bool InheritToChildren { get; set; } = true;
    
    public DateTime? ValidFrom { get; set; }
    
    public DateTime? ValidTo { get; set; }
    
    [StringLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for updating an existing access permission.
/// </summary>
public class UpdateUserAccessDto
{
    [Required]
    public Guid Id { get; set; }
    
    [Range(0, 4)]
    public byte AccessLevel { get; set; }
    
    public bool InheritToChildren { get; set; }
    
    public DateTime? ValidFrom { get; set; }
    
    public DateTime? ValidTo { get; set; }
    
    [StringLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for organizational hierarchy tree node.
/// Mapped from sp_OrganizationTree_WithUserAccess stored procedure.
/// </summary>
public class OrganizationTreeNode
{
    // SP column: NodeType (GROUP, COMPANY, WORKPLACE, LOCATION)
    public string NodeType { get; set; } = string.Empty;
    
    // SP column: NodeId (GUID as string)
    public string NodeId { get; set; } = string.Empty;
    
    // SP column: ParentId (GUID as string, null for root)
    public string? ParentId { get; set; }
    
    // SP column: Denumire
    public string Denumire { get; set; } = string.Empty;
    
    // SP column: Cod
    public string? Cod { get; set; }
    
    // SP column: TipNode (type description)
    public string? TipNode { get; set; }
    
    // SP column: Level (0=Group, 1=Company, 2=WorkPlace, 3=Location)
    public int Level { get; set; }
    
    // SP column: SortOrder
    public int SortOrder { get; set; }
    
    // SP column: DirectAccessId
    public Guid? DirectAccessId { get; set; }
    
    // SP column: DirectAccessLevel
    public byte? DirectAccessLevel { get; set; }
    
    // SP column: InheritToChildren
    public bool? InheritToChildren { get; set; }
    
    // SP column: EffectiveAccessLevel
    public byte? EffectiveAccessLevel { get; set; }
    
    // SP column: AccessType (Direct, Inherited, None)
    public string AccessType { get; set; } = "None";
    
    // Computed properties for UI
    public Guid Id => Guid.TryParse(NodeId, out var id) ? id : Guid.Empty;
    public string Name => Denumire;
    public string EntityType => NodeType;
    public bool HasChildren => Level < 3; // Groups, Companies, WorkPlaces have children
    public bool IsExpanded { get; set; } = true;
    
    // UI helpers
    public string Icon => NodeType switch
    {
        "GROUP" => "e-folder",
        "COMPANY" => "e-building",
        "WORKPLACE" => "e-location",
        "LOCATION" => "e-pin",
        _ => "e-circle"
    };
    
    public string AccessLevelBadge => EffectiveAccessLevel switch
    {
        0 => "danger",
        1 => "info",
        2 => "warning",
        3 => "success",
        4 => "primary",
        _ => "secondary"
    };
    
    public string AccessLevelName => EffectiveAccessLevel switch
    {
        0 => "Fără Acces",
        1 => "Citire",
        2 => "Scriere",
        3 => "Complet",
        4 => "Admin",
        _ => "Nedefinit"
    };
}

/// <summary>
/// DTO for user perimeter summary.
/// </summary>
public class UserPerimeterSummaryDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public int GroupCount { get; set; }
    public int CompanyCount { get; set; }
    public int WorkPlaceCount { get; set; }
    public int LocationCount { get; set; }
    public int TotalAccessRecords { get; set; }
    public DateTime? LastAccessUpdate { get; set; }
}

/// <summary>
/// Constants for access levels.
/// </summary>
public static class AccessLevels
{
    public const byte NoAccess = 0;
    public const byte Read = 1;
    public const byte Write = 2;
    public const byte Full = 3;
    public const byte Admin = 4;
    
    public static string GetName(byte level) => level switch
    {
        0 => "Fără Acces",
        1 => "Citire",
        2 => "Scriere",
        3 => "Complet",
        4 => "Administrator",
        _ => "Necunoscut"
    };
    
    public static IEnumerable<(byte Value, string Name)> All =>
    [
        (0, "Fără Acces (Deny)"),
        (1, "Citire"),
        (2, "Scriere"),
        (3, "Complet (include ștergere)"),
        (4, "Administrator")
    ];
}

/// <summary>
/// Constants for entity types.
/// </summary>
public static class EntityTypes
{
    public const string Group = "GROUP";
    public const string Company = "COMPANY";
    public const string WorkPlace = "WORKPLACE";
    public const string Location = "LOCATION";
    
    public static string GetDisplayName(string type) => type switch
    {
        "GROUP" => "Grup",
        "COMPANY" => "Companie",
        "WORKPLACE" => "Punct de Lucru",
        "LOCATION" => "Locație",
        _ => type
    };
    
    public static IEnumerable<(string Value, string Name)> All =>
    [
        ("GROUP", "Grup"),
        ("COMPANY", "Companie"),
        ("WORKPLACE", "Punct de Lucru"),
        ("LOCATION", "Locație")
    ];
}
