using System;
using System.Collections.Generic;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Models;

/// <summary>
/// DTO for creating a new audit log entry with field-level details.
/// Used when logging changes from repository/service layer.
/// </summary>
public class AuditLogEntry
{
    /// <summary>
    /// Type of entity being audited (e.g., "Persoane", "Users").
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Unique identifier of the entity.
    /// </summary>
    public string EntityId { get; set; } = string.Empty;
    
    /// <summary>
    /// SQL table name.
    /// </summary>
    public string TableName { get; set; } = string.Empty;
    
    /// <summary>
    /// Operation type: "Create", "Update", or "Delete".
    /// </summary>
    public string OperationType { get; set; } = string.Empty;
    
    /// <summary>
    /// User ID performing the action.
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// User email (denormalized).
    /// </summary>
    public string UserEmail { get; set; } = string.Empty;
    
    /// <summary>
    /// User full name (optional).
    /// </summary>
    public string? UserFullName { get; set; }
    
    /// <summary>
    /// Session ID (optional).
    /// </summary>
    public Guid? SessionId { get; set; }
    
    /// <summary>
    /// IP address (optional).
    /// </summary>
    public string? IPAddress { get; set; }
    
    /// <summary>
    /// User agent (optional).
    /// </summary>
    public string? UserAgent { get; set; }
    
    /// <summary>
    /// Optional notes.
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Collection of field-level changes.
    /// </summary>
    public List<AuditFieldChange> ChangedFields { get; set; } = new();
}

/// <summary>
/// Represents a single field-level change for audit logging.
/// </summary>
public class AuditFieldChange
{
    /// <summary>
    /// Name of the field that changed.
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>
    /// Previous value (NULL for Create).
    /// </summary>
    public string? OldValue { get; set; }
    
    /// <summary>
    /// New value (NULL for Delete).
    /// </summary>
    public string? NewValue { get; set; }
    
    /// <summary>
    /// Data type of the field.
    /// </summary>
    public string DataType { get; set; } = "string";
    
    /// <summary>
    /// Whether this field is sensitive (will be masked).
    /// </summary>
    public bool IsSensitive { get; set; }
}
