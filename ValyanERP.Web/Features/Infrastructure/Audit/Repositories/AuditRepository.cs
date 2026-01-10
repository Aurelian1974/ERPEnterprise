using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Infrastructure.Audit.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Infrastructure.Audit.Repositories;

/// <summary>
/// Repository implementation for audit log data access using Dapper and stored procedures.
/// </summary>
public class AuditRepository : IAuditRepository
{
    private readonly DapperContext _context;
    private readonly ILogger<AuditRepository> _logger;

    public AuditRepository(DapperContext context, ILogger<AuditRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(AuditLogEntry entry)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            // Serialize changed fields to JSON
            var changedFieldsJson = entry.ChangedFields.Count > 0 
                ? JsonSerializer.Serialize(entry.ChangedFields) 
                : null;
            
            var result = await connection.QueryFirstAsync<dynamic>(
                "sp_AuditLogs_Create",
                new
                {
                    EntityType = entry.EntityType,
                    EntityId = entry.EntityId,
                    TableName = entry.TableName,
                    OperationType = entry.OperationType,
                    UserId = entry.UserId,
                    UserEmail = entry.UserEmail,
                    UserFullName = entry.UserFullName,
                    SessionId = entry.SessionId,
                    IPAddress = entry.IPAddress,
                    UserAgent = entry.UserAgent,
                    Notes = entry.Notes,
                    ChangedFields = changedFieldsJson
                },
                commandType: CommandType.StoredProcedure);
            
            return result.AuditLogId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating audit log for {EntityType} {EntityId}", 
                entry.EntityType, entry.EntityId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PagedAuditResult<AuditLog>> GetByEntityAsync(string entityType, string entityId, int pageNumber = 1, int pageSize = 50)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            using var multi = await connection.QueryMultipleAsync(
                "sp_AuditLogs_GetByEntity",
                new { EntityType = entityType, EntityId = entityId, PageNumber = pageNumber, PageSize = pageSize },
                commandType: CommandType.StoredProcedure);
            
            // Read audit logs as dynamic to extract pagination info
            var logsData = (await multi.ReadAsync<dynamic>()).ToList();
            
            // Read details
            var details = (await multi.ReadAsync<AuditLogDetail>()).ToList();
            
            // Extract pagination info from first row
            int totalCount = 0;
            int currentPage = pageNumber;
            int totalPages = 0;
            
            if (logsData.Count > 0)
            {
                var first = logsData[0];
                totalCount = (int)first.TotalCount;
                currentPage = (int)first.CurrentPage;
                totalPages = (int)first.TotalPages;
            }
            
            // Map dynamic to AuditLog
            var logs = logsData.Select(d => new AuditLog
            {
                Id = (Guid)d.Id,
                EntityType = (string)d.EntityType,
                EntityId = (string)d.EntityId,
                TableName = (string)d.TableName,
                OperationType = (string)d.OperationType,
                UserId = (Guid)d.UserId,
                UserEmail = (string)d.UserEmail,
                UserFullName = d.UserFullName,
                SessionId = d.SessionId,
                IPAddress = d.IPAddress,
                UserAgent = d.UserAgent,
                Timestamp = (DateTime)d.Timestamp,
                ChangedFieldsCount = (int)d.ChangedFieldsCount,
                Notes = d.Notes,
                Details = details.Where(det => det.AuditLogId == (Guid)d.Id).ToList()
            }).ToList();
            
            return new PagedAuditResult<AuditLog>
            {
                Items = logs,
                TotalCount = totalCount,
                CurrentPage = currentPage,
                TotalPages = totalPages,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit logs for {EntityType} {EntityId}", entityType, entityId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PagedAuditResult<AuditLog>> GetByUserAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, int pageNumber = 1, int pageSize = 50)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var logs = (await connection.QueryAsync<dynamic>(
                "sp_AuditLogs_GetByUser",
                new { UserId = userId, StartDate = startDate, EndDate = endDate, PageNumber = pageNumber, PageSize = pageSize },
                commandType: CommandType.StoredProcedure)).ToList();
            
            // Extract pagination info
            int totalCount = 0;
            int currentPage = pageNumber;
            int totalPages = 0;
            
            if (logs.Count > 0)
            {
                var first = logs[0];
                totalCount = (int)first.TotalCount;
                currentPage = (int)first.CurrentPage;
                totalPages = (int)first.TotalPages;
            }
            
            var auditLogs = logs.Select(l => new AuditLog
            {
                Id = (Guid)l.Id,
                EntityType = (string)l.EntityType,
                EntityId = (string)l.EntityId,
                TableName = (string)l.TableName,
                OperationType = (string)l.OperationType,
                UserId = (Guid)l.UserId,
                UserEmail = (string)l.UserEmail,
                UserFullName = l.UserFullName,
                SessionId = l.SessionId,
                IPAddress = l.IPAddress,
                UserAgent = l.UserAgent,
                Timestamp = (DateTime)l.Timestamp,
                ChangedFieldsCount = (int)l.ChangedFieldsCount,
                Notes = l.Notes
            }).ToList();
            
            return new PagedAuditResult<AuditLog>
            {
                Items = auditLogs,
                TotalCount = totalCount,
                CurrentPage = currentPage,
                TotalPages = totalPages,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit logs for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AuditLog>> GetBySessionAsync(Guid sessionId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            using var multi = await connection.QueryMultipleAsync(
                "sp_AuditLogs_GetBySession",
                new { SessionId = sessionId },
                commandType: CommandType.StoredProcedure);
            
            var logs = (await multi.ReadAsync<AuditLog>()).ToList();
            var details = (await multi.ReadAsync<AuditLogDetail>()).ToList();
            
            foreach (var log in logs)
            {
                log.Details = details.Where(d => d.AuditLogId == log.Id).ToList();
            }
            
            return logs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit logs for session {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AuditLog?> GetByIdAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            using var multi = await connection.QueryMultipleAsync(
                "sp_AuditLogs_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
            
            var log = await multi.ReadFirstOrDefaultAsync<AuditLog>();
            if (log != null)
            {
                log.Details = (await multi.ReadAsync<AuditLogDetail>()).ToList();
            }
            
            return log;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit log {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<AuditStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            using var multi = await connection.QueryMultipleAsync(
                "sp_AuditLogs_GetStatistics",
                new { StartDate = startDate, EndDate = endDate },
                commandType: CommandType.StoredProcedure);
            
            var statistics = new AuditStatistics
            {
                ByOperationType = (await multi.ReadAsync<OperationTypeStatistic>()).ToList(),
                ByEntityType = (await multi.ReadAsync<EntityTypeStatistic>()).ToList(),
                TopUsers = (await multi.ReadAsync<UserActivityStatistic>()).ToList(),
                DailyChanges = (await multi.ReadAsync<DailyChangeStatistic>()).ToList()
            };
            
            // Read summary stats
            var summary = await multi.ReadFirstOrDefaultAsync<dynamic>();
            if (summary != null)
            {
                statistics.TotalChanges = summary.TotalChanges;
                statistics.UniqueUsers = summary.UniqueUsers;
                statistics.UniqueSessions = summary.UniqueSessions;
                statistics.EntityTypesModified = summary.EntityTypesModified;
                statistics.FirstChange = summary.FirstChange;
                statistics.LastChange = summary.LastChange;
            }
            
            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit statistics");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PagedAuditResult<AuditLog>> SearchAsync(
        string? entityType = null,
        Guid? userId = null,
        string? operationType = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string? searchTerm = null,
        int pageNumber = 1,
        int pageSize = 50)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var logs = (await connection.QueryAsync<dynamic>(
                "sp_AuditLogs_Search",
                new
                {
                    EntityType = entityType,
                    UserId = userId,
                    OperationType = operationType,
                    StartDate = startDate,
                    EndDate = endDate,
                    SearchTerm = searchTerm,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                },
                commandType: CommandType.StoredProcedure)).ToList();
            
            // Extract pagination info
            int totalCount = 0;
            int currentPage = pageNumber;
            int totalPages = 0;
            
            if (logs.Count > 0)
            {
                var first = logs[0];
                totalCount = (int)first.TotalCount;
                currentPage = (int)first.CurrentPage;
                totalPages = (int)first.TotalPages;
            }
            
            var auditLogs = logs.Select(l => new AuditLog
            {
                Id = (Guid)l.Id,
                EntityType = (string)l.EntityType,
                EntityId = (string)l.EntityId,
                TableName = (string)l.TableName,
                OperationType = (string)l.OperationType,
                UserId = (Guid)l.UserId,
                UserEmail = (string)l.UserEmail,
                UserFullName = l.UserFullName,
                SessionId = l.SessionId,
                IPAddress = l.IPAddress,
                UserAgent = l.UserAgent,
                Timestamp = (DateTime)l.Timestamp,
                ChangedFieldsCount = (int)l.ChangedFieldsCount,
                Notes = l.Notes
            }).ToList();
            
            return new PagedAuditResult<AuditLog>
            {
                Items = auditLogs,
                TotalCount = totalCount,
                CurrentPage = currentPage,
                TotalPages = totalPages,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching audit logs");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PagedAuditResult<AuditLog>> GetUserOwnActivityAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, int pageNumber = 1, int pageSize = 50)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            using var multi = await connection.QueryMultipleAsync(
                "sp_AuditLogs_GetUserOwnActivity",
                new { UserId = userId, StartDate = startDate, EndDate = endDate, PageNumber = pageNumber, PageSize = pageSize },
                commandType: CommandType.StoredProcedure);
            
            var logsData = (await multi.ReadAsync<dynamic>()).ToList();
            var details = (await multi.ReadAsync<AuditLogDetail>()).ToList();
            
            // Extract pagination info
            int totalCount = 0;
            int currentPage = pageNumber;
            int totalPages = 0;
            
            if (logsData.Count > 0)
            {
                var first = logsData[0];
                totalCount = (int)first.TotalCount;
                currentPage = (int)first.CurrentPage;
                totalPages = (int)first.TotalPages;
            }
            
            var logs = logsData.Select(d => new AuditLog
            {
                Id = (Guid)d.Id,
                EntityType = (string)d.EntityType,
                EntityId = (string)d.EntityId,
                TableName = (string)d.TableName,
                OperationType = (string)d.OperationType,
                Timestamp = (DateTime)d.Timestamp,
                ChangedFieldsCount = (int)d.ChangedFieldsCount,
                Notes = d.Notes,
                Details = details.Where(det => det.AuditLogId == (Guid)d.Id).ToList()
            }).ToList();
            
            return new PagedAuditResult<AuditLog>
            {
                Items = logs,
                TotalCount = totalCount,
                CurrentPage = currentPage,
                TotalPages = totalPages,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user own activity for {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> DeleteOldLogsAsync(int retentionDays)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryFirstAsync<dynamic>(
                "sp_AuditLogs_DeleteOld",
                new { RetentionDays = retentionDays },
                commandType: CommandType.StoredProcedure);
            
            int deletedCount = (int)result.DeletedAuditLogs;
            
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting old audit logs");
            throw;
        }
    }
}
