using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Infrastructure.Audit.Models;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Repositories;

/// <summary>
/// Repository interface for audit log data access operations.
/// All methods use stored procedures for optimal performance and security.
/// </summary>
public interface IAuditRepository
{
    /// <summary>
    /// Creates a new audit log entry with field-level details.
    /// </summary>
    /// <param name="entry">The audit log entry to create.</param>
    /// <returns>The GUID of the newly created audit log.</returns>
    Task<Guid> CreateAsync(AuditLogEntry entry);
    
    /// <summary>
    /// Retrieves audit logs for a specific entity with pagination.
    /// </summary>
    /// <param name="entityType">Type of entity (e.g., "Persoane").</param>
    /// <param name="entityId">Unique identifier of the entity.</param>
    /// <param name="pageNumber">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <returns>Paginated audit logs with details.</returns>
    Task<PagedAuditResult<AuditLog>> GetByEntityAsync(string entityType, string entityId, int pageNumber = 1, int pageSize = 50);
    
    /// <summary>
    /// Retrieves audit logs for a specific user with pagination.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="startDate">Start date filter (optional).</param>
    /// <param name="endDate">End date filter (optional).</param>
    /// <param name="pageNumber">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <returns>Paginated audit logs.</returns>
    Task<PagedAuditResult<AuditLog>> GetByUserAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, int pageNumber = 1, int pageSize = 50);
    
    /// <summary>
    /// Retrieves all audit logs for a specific session (typically small result set).
    /// </summary>
    /// <param name="sessionId">Session ID.</param>
    /// <returns>All audit logs for the session with details.</returns>
    Task<IEnumerable<AuditLog>> GetBySessionAsync(Guid sessionId);
    
    /// <summary>
    /// Retrieves a single audit log by ID with all details.
    /// </summary>
    /// <param name="id">Audit log ID.</param>
    /// <returns>The audit log or null if not found.</returns>
    Task<AuditLog?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Retrieves comprehensive audit statistics for dashboard and reporting.
    /// </summary>
    /// <param name="startDate">Start date filter (optional, defaults to last 30 days).</param>
    /// <param name="endDate">End date filter (optional, defaults to today).</param>
    /// <returns>Audit statistics.</returns>
    Task<AuditStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null);
    
    /// <summary>
    /// Advanced search with multiple filters and pagination.
    /// </summary>
    /// <param name="entityType">Entity type filter (optional).</param>
    /// <param name="userId">User ID filter (optional).</param>
    /// <param name="operationType">Operation type filter (optional).</param>
    /// <param name="startDate">Start date filter (optional).</param>
    /// <param name="endDate">End date filter (optional).</param>
    /// <param name="searchTerm">Search term for EntityId, UserEmail, UserFullName, Notes (optional).</param>
    /// <param name="pageNumber">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <returns>Paginated search results.</returns>
    Task<PagedAuditResult<AuditLog>> SearchAsync(
        string? entityType = null,
        Guid? userId = null,
        string? operationType = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? searchTerm = null,
        int pageNumber = 1,
        int pageSize = 50);
    
    /// <summary>
    /// Retrieves user's own audit activity (privacy-compliant, excludes IP/UserAgent).
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="startDate">Start date filter (optional).</param>
    /// <param name="endDate">End date filter (optional).</param>
    /// <param name="pageNumber">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <returns>Paginated user activity.</returns>
    Task<PagedAuditResult<AuditLog>> GetUserOwnActivityAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, int pageNumber = 1, int pageSize = 50);
    
    /// <summary>
    /// Deletes old audit logs based on retention policy.
    /// </summary>
    /// <param name="retentionDays">Number of days to retain (older logs will be deleted).</param>
    /// <returns>Number of deleted audit logs.</returns>
    Task<int> DeleteOldLogsAsync(int retentionDays);
}
