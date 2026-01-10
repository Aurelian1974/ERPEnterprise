// ================================================================
// FILE: SystemParametersRepository.cs
// PURPOSE: Dapper repository for system parameters data access
// ARCHITECTURE: Repository Pattern with stored procedures
// ================================================================

using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Infrastructure.SystemParameters.Models;
using ValyanERP.Web.Infrastructure.Data;
using ValyanERP.Web.Features.Infrastructure.Audit.Services;

namespace ValyanERP.Web.Features.Infrastructure.SystemParameters.Repositories;

/// <summary>
/// Repository implementation for system parameters using Dapper and stored procedures.
/// Includes automatic audit logging for Update operations to track configuration changes.
/// </summary>
public class SystemParametersRepository : ISystemParametersRepository
{
    private readonly DapperContext _context;
    private readonly ILogger<SystemParametersRepository> _logger;
    private readonly IAuditService _auditService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationStateProvider _authStateProvider;

    public SystemParametersRepository(
        DapperContext context,
        ILogger<SystemParametersRepository> logger,
        IAuditService auditService,
        IHttpContextAccessor httpContextAccessor,
        AuthenticationStateProvider authStateProvider)
    {
        _context = context;
        _logger = logger;
        _auditService = auditService;
        _httpContextAccessor = httpContextAccessor;
        _authStateProvider = authStateProvider;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SystemParameter>> GetAllAsync(
        string? category = null,
        string? subCategory = null,
        bool includeReadOnly = true)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = await connection.QueryAsync<SystemParameter>(
                "sp_SystemParameters_GetAll",
                new { Category = category, SubCategory = subCategory, IncludeReadOnly = includeReadOnly },
                commandType: CommandType.StoredProcedure);

            return parameters;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error retrieving system parameters (Category: {Category}, SubCategory: {SubCategory})",
                category ?? "All", subCategory ?? "All");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SystemParameter?> GetByKeyAsync(string parameterKey)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameter = await connection.QueryFirstOrDefaultAsync<SystemParameter>(
                "sp_SystemParameters_GetByKey",
                new { ParameterKey = parameterKey },
                commandType: CommandType.StoredProcedure);

            if (parameter == null)
            {
                _logger.LogWarning("Parameter not found: {ParameterKey}", parameterKey);
            }

            return parameter;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving parameter: {ParameterKey}", parameterKey);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<SystemParameter>> GetByCategoryAsync(string category)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = await connection.QueryAsync<SystemParameter>(
                "sp_SystemParameters_GetByCategory",
                new { Category = category },
                commandType: CommandType.StoredProcedure);

            return parameters;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving parameters for category: {Category}", category);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(SystemParameter parameter, Guid? createdBy = null)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryFirstAsync<Guid>(
                "sp_SystemParameters_Create",
                new
                {
                    Id = parameter.Id == Guid.Empty ? (Guid?)null : parameter.Id,
                    parameter.ParameterKey,
                    parameter.Category,
                    parameter.SubCategory,
                    parameter.ParameterValue,
                    parameter.DataType,
                    parameter.DefaultValue,
                    parameter.Description,
                    parameter.DisplayName,
                    parameter.IsReadOnly,
                    parameter.IsEncrypted,
                    parameter.ValidationRegex,
                    parameter.MinValue,
                    parameter.MaxValue,
                    CreatedBy = createdBy
                },
                commandType: CommandType.StoredProcedure);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Error creating system parameter: {ParameterKey}",
                parameter.ParameterKey);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(SystemParameter parameter, Guid? updatedBy = null)
    {
        try
        {
            // Get old value for audit diff (fetch by ParameterKey since GetByIdAsync doesn't exist)
            var oldValue = await GetByKeyAsync(parameter.ParameterKey);
            
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteScalarAsync<int>(
                "sp_SystemParameters_Update",
                new
                {
                    parameter.Id,
                    parameter.ParameterValue,
                    parameter.DefaultValue,
                    parameter.Description,
                    parameter.DisplayName,
                    parameter.ValidationRegex,
                    parameter.MinValue,
                    parameter.MaxValue,
                    UpdatedBy = updatedBy
                },
                commandType: CommandType.StoredProcedure);

            var success = rowsAffected > 0;

            if (success)
            {
                // Auto-audit: Log Update operation (CRITICAL for config changes)
                if (oldValue != null)
                {
                    await LogAuditAsync("Update", parameter.Id.ToString(), parameter, oldValue);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Failed to update parameter (not found or read-only): {Id}",
                    parameter.Id);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating system parameter: {Id}", parameter.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, Guid? updatedBy = null)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteScalarAsync<int>(
                "sp_SystemParameters_Delete",
                new { Id = id, UpdatedBy = updatedBy },
                commandType: CommandType.StoredProcedure);

            var success = rowsAffected > 0;

            if (!success)
            {
                _logger.LogWarning(
                    "Failed to delete parameter (not found or read-only): {Id}",
                    id);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting system parameter: {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<(string Category, int ParameterCount)>> GetCategoriesAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var categories = await connection.QueryAsync<CategoryCount>(
                "sp_SystemParameters_GetCategories",
                commandType: CommandType.StoredProcedure);

            var result = categories.Select(c => (c.Category, c.ParameterCount)).ToList();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving parameter categories");
            throw;
        }
    }

    #region Private Audit Helper Methods

    /// <summary>
    /// Logs audit trail for Update operations on system parameters (CRITICAL for config tracking)
    /// </summary>
    private async Task LogAuditAsync(string operationType, string entityId, SystemParameter newValue, SystemParameter oldValue)
    {
        try
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == Guid.Empty)
            {
                _logger.LogWarning("Skipping audit log for SystemParameter update - no authenticated user found. Parameter: {Key}", newValue.ParameterKey);
                return;
            }

            var sessionId = GetCurrentSessionId();
            var ipAddress = GetClientIP();
            var userAgent = GetUserAgent();

            // SystemParameters only support Update operation (Create/Delete done via migrations)
            if (operationType == "Update")
            {
                await _auditService.LogUpdateAsync(
                    "SystemParameters",
                    entityId,
                    oldValue,
                    newValue,
                    userId,
                    sessionId,
                    ipAddress,
                    userAgent,
                    notes: $"Config change: {newValue.ParameterKey}"
                );
            }
        }
        catch (Exception ex)
        {
            // Don't fail the operation if audit fails
            _logger.LogError(ex, "Failed to create audit log for {OperationType} on SystemParameters Id={EntityId}", 
                operationType, entityId);
        }
    }

    /// <summary>
    /// Extracts current user ID from AuthenticationState (Blazor Server compatible)
    /// </summary>
    private async Task<Guid> GetCurrentUserIdAsync()
    {
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;

            if (user?.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier);
                if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    return userId;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user ID from AuthenticationState");
        }

        return Guid.Empty;
    }

    /// <summary>
    /// Extracts current session ID from claims or session storage
    /// </summary>
    private Guid? GetCurrentSessionId()
    {
        try
        {
            var sessionClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("SessionId");
            if (sessionClaim != null && Guid.TryParse(sessionClaim.Value, out var sessionId))
            {
                return sessionId;
            }

            // Try to get from session storage (may not be available in Blazor Server)
            try
            {
                var sessionIdString = _httpContextAccessor.HttpContext?.Session?.GetString("SessionId");
                if (!string.IsNullOrEmpty(sessionIdString) && Guid.TryParse(sessionIdString, out var sessionIdFromStorage))
                {
                    return sessionIdFromStorage;
                }
            }
            catch (InvalidOperationException)
            {
                // Session not configured in Blazor Server - this is expected
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error retrieving SessionId - continuing with null");
        }

        return null;
    }

    /// <summary>
    /// Extracts client IP address from HttpContext
    /// </summary>
    private string? GetClientIP()
    {
        return _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Extracts User-Agent from request headers
    /// </summary>
    private string? GetUserAgent()
    {
        return _httpContextAccessor.HttpContext?.Request?.Headers["User-Agent"].ToString();
    }

    #endregion

    /// <summary>
    /// DTO for mapping category query results.
    /// </summary>
    private class CategoryCount
    {
        public string Category { get; set; } = string.Empty;
        public int ParameterCount { get; set; }
    }
}
