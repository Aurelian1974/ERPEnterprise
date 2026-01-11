using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Infrastructure.GridSettings.Models;

/// <summary>
/// Model for storing user-specific grid configuration.
/// Persisted to database for each user/grid combination.
/// </summary>
public class UserGridSettings
{
    /// <summary>
    /// Unique identifier for the grid settings record.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// The user ID who owns this configuration.
    /// </summary>
    [Required]
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Unique identifier for the grid (e.g., "Utilizatori", "Persoane").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string GridId { get; set; } = string.Empty;
    
    /// <summary>
    /// Name of the configuration (e.g., "Default", "Compact View").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ConfigurationName { get; set; } = "Default";
    
    /// <summary>
    /// JSON string containing the grid state (columns, filters, sorts, groups).
    /// Serialized from Syncfusion Grid's GetPersistDataAsync().
    /// </summary>
    public string? GridState { get; set; }
    
    /// <summary>
    /// Whether this is the default configuration for this user/grid.
    /// </summary>
    public bool IsDefault { get; set; }
    
    /// <summary>
    /// Whether this configuration is active.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    /// <summary>
    /// When the settings were created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the settings were last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// DTO for creating new grid settings.
/// </summary>
public class CreateUserGridSettingsDto
{
    [Required]
    public Guid UserId { get; set; }
    
    [Required]
    [MaxLength(100)]
    public string GridId { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(100)]
    public string ConfigurationName { get; set; } = "Default";
    
    public string? GridState { get; set; }
    
    public bool IsDefault { get; set; }
}

/// <summary>
/// DTO for updating existing grid settings.
/// </summary>
public class UpdateUserGridSettingsDto
{
    [Required]
    public Guid Id { get; set; }
    
    public string? GridState { get; set; }
    
    public bool IsDefault { get; set; }
}
