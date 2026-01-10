using System;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Models;

/// <summary>
/// Represents a field-level change detail within an audit log.
/// Maps to [dbo].[AuditLogDetails] table.
/// </summary>
public class AuditLogDetail
{
    /// <summary>
    /// Unique identifier for the audit log detail.
    /// </summary>
    public Guid Id { get; set; }
    
    /// <summary>
    /// Foreign key to the parent AuditLog.
    /// </summary>
    public Guid AuditLogId { get; set; }
    
    /// <summary>
    /// Name of the field that changed (e.g., "FirstName", "Email", "CNP").
    /// </summary>
    public string FieldName { get; set; } = string.Empty;
    
    /// <summary>
    /// Previous value before the change (NULL for Create operations, masked if sensitive).
    /// </summary>
    public string? OldValue { get; set; }
    
    /// <summary>
    /// New value after the change (NULL for Delete operations, masked if sensitive).
    /// </summary>
    public string? NewValue { get; set; }
    
    /// <summary>
    /// Data type of the field (e.g., "string", "int", "datetime", "bool", "guid").
    /// </summary>
    public string? DataType { get; set; }
    
    /// <summary>
    /// Indicates whether this field contains sensitive data that was masked.
    /// </summary>
    public bool IsSensitive { get; set; }
}
