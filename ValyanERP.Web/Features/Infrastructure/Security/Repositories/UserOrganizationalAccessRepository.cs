// ========================================================================
// UserOrganizationalAccessRepository.cs
// Repository implementation for UserOrganizationalAccess CRUD operations
// Uses stored procedures from 023_StoredProcedures_OrganizationalAccess.sql
// ========================================================================

using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ValyanERP.Web.Features.Infrastructure.Security.Models;
using ValyanERP.Web.Features.Infrastructure.Security.Models.DTOs;
using ValyanERP.Web.Features.Infrastructure.Security.Services;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Infrastructure.Security.Repositories;

/// <summary>
/// Repository implementation for managing user organizational access permissions.
/// All operations use stored procedures for security and performance.
/// </summary>
public class UserOrganizationalAccessRepository : IUserOrganizationalAccessRepository
{
    private readonly DapperContext _context;
    private readonly IUserPerimeterProvider _perimeterProvider;
    private readonly ILogger<UserOrganizationalAccessRepository> _logger;

    public UserOrganizationalAccessRepository(
        DapperContext context,
        IUserPerimeterProvider perimeterProvider,
        ILogger<UserOrganizationalAccessRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _perimeterProvider = perimeterProvider ?? throw new ArgumentNullException(nameof(perimeterProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region CRUD Operations

    /// <inheritdoc />
    public async Task<IEnumerable<UserAccessListDto>> GetByUserIdAsync(Guid userId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryAsync<UserAccessListDto>(
                "sp_UserOrganizationalAccess_GetByUser",
                new { UserId = userId },
                commandType: CommandType.StoredProcedure);
            
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error getting access records for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<UserOrganizationalAccess?> GetByIdAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryFirstOrDefaultAsync<UserOrganizationalAccess>(
                "sp_UserOrganizationalAccess_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
            
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error getting access record {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(CreateUserAccessDto dto, Guid createdBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@UserId", dto.UserId);
            parameters.Add("@EntityType", dto.EntityType);
            parameters.Add("@EntityId", dto.EntityId);
            parameters.Add("@AccessLevel", dto.AccessLevel);
            parameters.Add("@InheritToChildren", dto.InheritToChildren);
            parameters.Add("@ValidFrom", dto.ValidFrom);
            parameters.Add("@ValidTo", dto.ValidTo);
            parameters.Add("@Notes", dto.Notes);
            parameters.Add("@CreatedBy", createdBy);
            parameters.Add("@NewId", dbType: DbType.Guid, direction: ParameterDirection.Output);
            
            var result = await connection.QueryFirstAsync<dynamic>(
                "sp_UserOrganizationalAccess_Create",
                parameters,
                commandType: CommandType.StoredProcedure);
            
            var newId = (Guid)result.NewId;
            
            _logger.LogInformation(
                "Created access for user {UserId} to {EntityType} {EntityId} with level {AccessLevel}",
                dto.UserId, dto.EntityType, dto.EntityId, dto.AccessLevel);
            
            // Invalidate user's perimeter cache
            await _perimeterProvider.InvalidateCacheForUserAsync(dto.UserId);
            
            return newId;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error creating access for user {UserId}", dto.UserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(UpdateUserAccessDto dto, Guid updatedBy)
    {
        try
        {
            // Get existing record to find user ID for cache invalidation
            var existing = await GetByIdAsync(dto.Id);
            if (existing == null)
            {
                _logger.LogWarning("Access record {Id} not found for update", dto.Id);
                return false;
            }
            
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@Id", dto.Id);
            parameters.Add("@AccessLevel", dto.AccessLevel);
            parameters.Add("@InheritToChildren", dto.InheritToChildren);
            parameters.Add("@ValidFrom", dto.ValidFrom);
            parameters.Add("@ValidTo", dto.ValidTo);
            parameters.Add("@Notes", dto.Notes);
            parameters.Add("@UpdatedBy", updatedBy);
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_UserOrganizationalAccess_Update",
                parameters,
                commandType: CommandType.StoredProcedure);
            
            if (rowsAffected > 0)
            {
                _logger.LogInformation("Updated access record {Id}", dto.Id);
                
                // Invalidate user's perimeter cache
                await _perimeterProvider.InvalidateCacheForUserAsync(existing.UserId);
                
                return true;
            }
            
            return false;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error updating access record {Id}", dto.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, Guid deletedBy)
    {
        try
        {
            // Get existing record to find user ID for cache invalidation
            var existing = await GetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning("Access record {Id} not found for deletion", id);
                return false;
            }
            
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_UserOrganizationalAccess_Delete",
                new { Id = id, UpdatedBy = deletedBy },
                commandType: CommandType.StoredProcedure);
            
            if (rowsAffected > 0)
            {
                _logger.LogInformation("Revoked access record {Id} for user {UserId}", id, existing.UserId);
                
                // Invalidate user's perimeter cache
                await _perimeterProvider.InvalidateCacheForUserAsync(existing.UserId);
                
                return true;
            }
            
            return false;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error deleting access record {Id}", id);
            throw;
        }
    }

    #endregion

    #region Bulk Operations

    /// <inheritdoc />
    public async Task<int> BulkCreateAsync(IEnumerable<CreateUserAccessDto> dtos, Guid createdBy)
    {
        var dtoList = dtos.ToList();
        if (!dtoList.Any()) return 0;
        
        try
        {
            using var connection = _context.CreateConnection();
            
            // Create DataTable for TVP
            var dt = new DataTable();
            dt.Columns.Add("UserId", typeof(Guid));
            dt.Columns.Add("EntityType", typeof(string));
            dt.Columns.Add("EntityId", typeof(Guid));
            dt.Columns.Add("AccessLevel", typeof(byte));
            dt.Columns.Add("InheritToChildren", typeof(bool));
            dt.Columns.Add("ValidFrom", typeof(DateTime));
            dt.Columns.Add("ValidTo", typeof(DateTime));
            dt.Columns.Add("Notes", typeof(string));
            
            foreach (var dto in dtoList)
            {
                dt.Rows.Add(
                    dto.UserId,
                    dto.EntityType,
                    dto.EntityId,
                    dto.AccessLevel,
                    dto.InheritToChildren,
                    dto.ValidFrom ?? (object)DBNull.Value,
                    dto.ValidTo ?? (object)DBNull.Value,
                    dto.Notes ?? (object)DBNull.Value);
            }
            
            var parameters = new DynamicParameters();
            parameters.Add("@AccessList", dt.AsTableValuedParameter("UserAccessBulkType"));
            parameters.Add("@CreatedBy", createdBy);
            
            var result = await connection.QueryFirstAsync<int>(
                "sp_UserOrganizationalAccess_BulkCreate",
                parameters,
                commandType: CommandType.StoredProcedure);
            
            // Invalidate cache for all affected users
            var affectedUsers = dtoList.Select(d => d.UserId).Distinct();
            foreach (var userId in affectedUsers)
            {
                await _perimeterProvider.InvalidateCacheForUserAsync(userId);
            }
            
            _logger.LogInformation("Bulk created {Count} access records", result);
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error in bulk create access records");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> CopyFromUserAsync(Guid sourceUserId, Guid targetUserId, Guid createdBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryFirstAsync<int>(
                "sp_UserOrganizationalAccess_CopyFromUser",
                new { SourceUserId = sourceUserId, TargetUserId = targetUserId, CreatedBy = createdBy },
                commandType: CommandType.StoredProcedure);
            
            // Invalidate target user's cache
            await _perimeterProvider.InvalidateCacheForUserAsync(targetUserId);
            
            _logger.LogInformation(
                "Copied {Count} access records from user {SourceUserId} to {TargetUserId}",
                result, sourceUserId, targetUserId);
            
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error copying access from {SourceUserId} to {TargetUserId}", 
                sourceUserId, targetUserId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> RevokeAllForUserAsync(Guid userId, Guid revokedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(@"
                UPDATE UserOrganizationalAccess 
                SET IsActive = 0, UpdatedAt = GETDATE(), UpdatedBy = @RevokedBy
                WHERE UserId = @UserId AND IsActive = 1",
                new { UserId = userId, RevokedBy = revokedBy });
            
            // Invalidate user's cache
            await _perimeterProvider.InvalidateCacheForUserAsync(userId);
            
            _logger.LogInformation("Revoked all ({Count}) access records for user {UserId}", 
                rowsAffected, userId);
            
            return rowsAffected;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error revoking all access for user {UserId}", userId);
            throw;
        }
    }

    #endregion

    #region Query Operations

    /// <inheritdoc />
    public async Task<IEnumerable<OrganizationTreeNode>> GetOrganizationTreeAsync(Guid? userId = null)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryAsync<OrganizationTreeNode>(
                "sp_OrganizationTree_WithUserAccess",
                new { UserId = userId },
                commandType: CommandType.StoredProcedure);
            
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error getting organization tree for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<UserAccessListDto>> GetUsersWithAccessAsync(string entityType, Guid entityId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryAsync<UserAccessListDto>(
                "sp_UserOrganizationalAccess_GetUsersWithAccess",
                new { EntityType = entityType, EntityId = entityId },
                commandType: CommandType.StoredProcedure);
            
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error getting users with access to {EntityType} {EntityId}", 
                entityType, entityId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<UserPerimeterSummaryDto?> GetUserPerimeterSummaryAsync(Guid userId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryFirstOrDefaultAsync<UserPerimeterSummaryDto>(
                "sp_UserOrganizationalAccess_GetPerimeterSummary",
                new { UserId = userId },
                commandType: CommandType.StoredProcedure);
            
            return result;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error getting perimeter summary for user {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<(bool HasAccess, byte AccessLevel)> CheckAccessAsync(
        Guid userId, string entityType, Guid entityId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "sp_CheckUserAccess",
                new { UserId = userId, EntityType = entityType, EntityId = entityId, RequiredAccessLevel = 1 },
                commandType: CommandType.StoredProcedure);
            
            if (result == null)
                return (false, 0);
            
            return ((bool)result.HasAccess, (byte)result.AccessLevel);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error checking access for user {UserId} to {EntityType} {EntityId}",
                userId, entityType, entityId);
            throw;
        }
    }

    #endregion

    #region Cache Operations

    /// <inheritdoc />
    public async Task RefreshUserCacheAsync(Guid userId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            await connection.ExecuteAsync(
                "sp_RefreshUserVisibleEntitiesCache",
                new { UserId = userId },
                commandType: CommandType.StoredProcedure);
            
            _logger.LogInformation("Refreshed visibility cache for user {UserId}", userId);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error refreshing cache for user {UserId}", userId);
            throw;
        }
    }

    #endregion
}
