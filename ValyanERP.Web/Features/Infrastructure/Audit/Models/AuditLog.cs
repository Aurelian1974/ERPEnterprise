using System;
using System.Collections.Generic;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Models;

/// <summary>
/// Represents a master audit log entry tracking WHO changed WHAT, WHEN, and WHERE.
/// Maps to [dbo].[AuditLogs] table.
/// </summary>
public class AuditLog
{
    /// <summary>
    /// Unique identifier for the audit log entry.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Type of entity being audited (e.g., "Persoane", "Users", "SystemParameters").
    /// </summary>
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Unique identifier of the entity being audited (flexible string for GUID or composite keys).
    /// </summary>
    public string EntityId { get; set; } = string.Empty;
    
    /// <summary>
    /// Actual SQL table name (e.g., "Persoane", "Users").
    /// </summary>
    public string TableName { get; set; } = string.Empty;
    
    /// <summary>
    /// Type of operation performed: "Create", "Update", or "Delete".
    /// </summary>
    public string OperationType { get; set; } = string.Empty;
    
    /// <summary>
    /// User ID who performed the action.
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// Email of the user who performed the action (denormalized for performance).
    /// </summary>
    public string UserEmail { get; set; } = string.Empty;
    
    /// <summary>
    /// Full name of the user who performed the action (denormalized for display).
    /// </summary>
    public string? UserFullName { get; set; }
    
    /// <summary>
    /// Session ID associated with the action (NULL for system operations).
    /// </summary>
    public Guid? SessionId { get; set; }
    
    /// <summary>
    /// IP address from which the action was performed.
    /// </summary>
    public string? IPAddress { get; set; }
    
    /// <summary>
    /// User agent (browser/client info) from which the action was performed.
    /// </summary>
    public string? UserAgent { get; set; }
    
    /// <summary>
    /// Timestamp when the action was performed (Bucharest timezone).
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Number of fields changed (for Update operations).
    /// </summary>
    public int ChangedFieldsCount { get; set; }
    
    /// <summary>
    /// Optional manual notes (e.g., "Bulk import", "Admin override").
    /// </summary>
    public string? Notes { get; set; }
    
    /// <summary>
    /// Timestamp when this audit log was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>
    /// Collection of field-level change details.
    /// </summary>
    public List<AuditLogDetail> Details { get; set; } = new();
}
