using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ValyanERP.Web.Features.Infrastructure.Audit.Models;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Services;

/// <summary>
/// Service interface for audit logging operations.
/// Provides high-level methods for logging CRUD operations and querying audit data.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs a Create operation.
    /// </summary>
    /// <typeparam name="T">Type of the entity being created.</typeparam>
    /// <param name="entityType">Type name of the entity (e.g., "Persoane").</param>
    /// <param name="entityId">Unique identifier of the created entity.</param>
    /// <param name="newValue">The newly created entity object.</param>
    /// <param name="userId">User ID performing the operation.</param>
    /// <param name="sessionId">Session ID (optional).</param>
    /// <param name="ipAddress">IP address (optional).</param>
    /// <param name="userAgent">User agent (optional).</param>
    /// <param name="notes">Optional notes.</param>
    /// <returns>The GUID of the audit log entry.</returns>
    Task<Guid> LogCreateAsync<T>(
        string entityType,
        string entityId,
        T newValue,
        Guid userId,
        Guid? sessionId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? notes = null) where T : class;

    /// <summary>
    /// Logs an Update operation with automatic field-level diff.
    /// </summary>
    /// <typeparam name="T">Type of the entity being updated.</typeparam>
    /// <param name="entityType">Type name of the entity.</param>
    /// <param name="entityId">Unique identifier of the updated entity.</param>
    /// <param name="oldValue">The entity before the update.</param>
    /// <param name="newValue">The entity after the update.</param>
    /// <param name="userId">User ID performing the operation.</param>
    /// <param name="sessionId">Session ID (optional).</param>
    /// <param name="ipAddress">IP address (optional).</param>
    /// <param name="userAgent">User agent (optional).</param>
    /// <param name="notes">Optional notes.</param>
    /// <returns>The GUID of the audit log entry.</returns>
    Task<Guid> LogUpdateAsync<T>(
        string entityType,
        string entityId,
        T oldValue,
        T newValue,
        Guid userId,
        Guid? sessionId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? notes = null) where T : class;

    /// <summary>
    /// Logs a Delete operation.
    /// </summary>
    /// <typeparam name="T">Type of the entity being deleted.</typeparam>
    /// <param name="entityType">Type name of the entity.</param>
    /// <param name="entityId">Unique identifier of the deleted entity.</param>
    /// <param name="deletedValue">The entity being deleted.</param>
    /// <param name="userId">User ID performing the operation.</param>
    /// <param name="sessionId">Session ID (optional).</param>
    /// <param name="ipAddress">IP address (optional).</param>
    /// <param name="userAgent">User agent (optional).</param>
    /// <param name="notes">Optional notes.</param>
    /// <returns>The GUID of the audit log entry.</returns>
    Task<Guid> LogDeleteAsync<T>(
        string entityType,
        string entityId,
        T deletedValue,
        Guid userId,
        Guid? sessionId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? notes = null) where T : class;

    /// <summary>
    /// Retrieves audit logs for a specific entity.
    /// </summary>
    Task<PagedAuditResult<AuditLog>> GetByEntityAsync(string entityType, string entityId, int pageNumber = 1, int pageSize = 50);

    /// <summary>
    /// Retrieves audit logs for a specific user.
    /// </summary>
    Task<PagedAuditResult<AuditLog>> GetByUserAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, int pageNumber = 1, int pageSize = 50);

    /// <summary>
    /// Retrieves audit logs for a specific session.
    /// </summary>
    Task<IEnumerable<AuditLog>> GetBySessionAsync(Guid sessionId);

    /// <summary>
    /// Retrieves a single audit log by ID.
    /// </summary>
    Task<AuditLog?> GetByIdAsync(Guid id);

    /// <summary>
    /// Retrieves comprehensive audit statistics.
    /// </summary>
    Task<AuditStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null);

    /// <summary>
    /// Advanced search with multiple filters.
    /// </summary>
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
    /// Retrieves user's own audit activity (privacy-compliant).
    /// </summary>
    Task<PagedAuditResult<AuditLog>> GetUserOwnActivityAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, int pageNumber = 1, int pageSize = 50);

    /// <summary>
    /// Deletes old audit logs based on retention policy.
    /// </summary>
    Task<int> DeleteOldLogsAsync(int retentionDays);
}
