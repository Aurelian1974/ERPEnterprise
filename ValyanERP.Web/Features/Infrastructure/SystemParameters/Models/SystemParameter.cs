// ================================================================
// FILE: SystemParameter.cs
// PURPOSE: Model for system configuration parameters
// ARCHITECTURE: Vertical Slices - Infrastructure feature
// ================================================================

namespace ValyanERP.Web.Features.Infrastructure.SystemParameters.Models;

/// <summary>
/// Represents a system configuration parameter stored in database.
/// Enables runtime configuration changes without code deployment.
/// </summary>
/// <remarks>
/// Parameters support various data types (int, string, bool, decimal, json, enum)
/// and include validation rules (regex, min/max values) for data integrity.
/// Read-only parameters are protected from modification/deletion.
/// </remarks>
public class SystemParameter
{
    /// <summary>
    /// Unique identifier for the parameter.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Unique key for parameter lookup (e.g., "Cache.Persoane.DurationMinutes").
    /// </summary>
    /// <remarks>
    /// Convention: Category.SubCategory.ParameterName
    /// Must be unique across all active parameters.
    /// </remarks>
    public string ParameterKey { get; set; } = string.Empty;

    /// <summary>
    /// Primary category for grouping (e.g., "Cache", "Validation", "Business").
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Optional sub-category for finer grouping (e.g., "Persoane", "Password").
    /// </summary>
    public string? SubCategory { get; set; }

    /// <summary>
    /// Current parameter value as string (converted based on DataType).
    /// </summary>
    public string ParameterValue { get; set; } = string.Empty;

    /// <summary>
    /// Data type of the parameter: int, string, bool, decimal, json, enum.
    /// </summary>
    /// <remarks>
    /// Used for type-safe conversion when reading parameter values.
    /// </remarks>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Default value used when parameter is not set or invalid.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Human-readable description explaining the parameter's purpose and impact.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown in admin UI (e.g., "Cache Duration (Minutes)").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// If true, parameter cannot be modified or deleted (system-critical).
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// If true, parameter value is encrypted (e.g., API keys, passwords).
    /// </summary>
    /// <remarks>
    /// Future implementation for sensitive data protection.
    /// </remarks>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// Regular expression for validating ParameterValue (optional).
    /// </summary>
    /// <remarks>
    /// Example: Email format, phone number format, specific patterns.
    /// </remarks>
    public string? ValidationRegex { get; set; }

    /// <summary>
    /// Minimum allowed value for numeric types (int, decimal).
    /// </summary>
    public decimal? MinValue { get; set; }

    /// <summary>
    /// Maximum allowed value for numeric types (int, decimal).
    /// </summary>
    public decimal? MaxValue { get; set; }

    /// <summary>
    /// Soft delete flag - if false, parameter is considered deleted.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// UTC timestamp when parameter was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// User who created this parameter.
    /// </summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>
    /// UTC timestamp when parameter was last modified.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// User who last modified this parameter.
    /// </summary>
    public Guid? UpdatedBy { get; set; }
}
