using System;
using System.Collections.Generic;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Models;

/// <summary>
/// Represents comprehensive audit statistics for dashboard and reporting.
/// </summary>
public class AuditStatistics
{
    /// <summary>
    /// Total number of changes in the period.
    /// </summary>
    public int TotalChanges { get; set; }
    
    /// <summary>
    /// Number of unique users who made changes.
    /// </summary>
    public int UniqueUsers { get; set; }
    
    /// <summary>
    /// Number of unique sessions with changes.
    /// </summary>
    public int UniqueSessions { get; set; }
    
    /// <summary>
    /// Number of different entity types modified.
    /// </summary>
    public int EntityTypesModified { get; set; }
    
    /// <summary>
    /// Timestamp of the first change in the period.
    /// </summary>
    public DateTime? FirstChange { get; set; }
    
    /// <summary>
    /// Timestamp of the last change in the period.
    /// </summary>
    public DateTime? LastChange { get; set; }
    
    /// <summary>
    /// Changes grouped by operation type (Create/Update/Delete).
    /// </summary>
    public List<OperationTypeStatistic> ByOperationType { get; set; } = new();
    
    /// <summary>
    /// Changes grouped by entity type (Persoane/Users/etc.).
    /// </summary>
    public List<EntityTypeStatistic> ByEntityType { get; set; } = new();
    
    /// <summary>
    /// Top 10 most active users.
    /// </summary>
    public List<UserActivityStatistic> TopUsers { get; set; } = new();
    
    /// <summary>
    /// Daily change counts for charting.
    /// </summary>
    public List<DailyChangeStatistic> DailyChanges { get; set; } = new();
}

/// <summary>
/// Statistics by operation type.
/// </summary>
public class OperationTypeStatistic
{
    public string OperationType { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// Statistics by entity type.
/// </summary>
public class EntityTypeStatistic
{
    public string EntityType { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// User activity statistics.
/// </summary>
public class UserActivityStatistic
{
    public Guid UserId { get; set; }
    public string UserEmail { get; set; } = string.Empty;
    public string? UserFullName { get; set; }
    public int ChangeCount { get; set; }
}

/// <summary>
/// Daily change statistics for time-series charts.
/// </summary>
public class DailyChangeStatistic
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}
