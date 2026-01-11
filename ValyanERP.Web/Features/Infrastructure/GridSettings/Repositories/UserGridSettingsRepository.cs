using Dapper;
using Microsoft.Extensions.Logging;
using System.Data;
using ValyanERP.Web.Features.Infrastructure.GridSettings.Models;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Infrastructure.GridSettings.Repositories;

/// <summary>
/// Repository for user grid settings data access using Dapper.
/// Uses stored procedures for all database operations.
/// ⚠️ ALL database access via stored procedures (sp_UserGridSettings_*)
/// </summary>
public class UserGridSettingsRepository : IUserGridSettingsRepository
{
    private readonly DapperContext _context;
    private readonly ILogger<UserGridSettingsRepository> _logger;

    public UserGridSettingsRepository(DapperContext context, ILogger<UserGridSettingsRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<UserGridSettings>> GetByUserAndGridAsync(Guid userId, string gridId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            return await connection.QueryAsync<UserGridSettings>(
                "sp_UserGridSettings_GetAll",
                new { UserId = userId, GridId = gridId },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting grid settings for User={UserId}, Grid={GridId}", userId, gridId);
            return Enumerable.Empty<UserGridSettings>();
        }
    }

    /// <inheritdoc />
    public async Task<UserGridSettings?> GetDefaultAsync(Guid userId, string gridId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            return await connection.QueryFirstOrDefaultAsync<UserGridSettings>(
                "sp_UserGridSettings_GetDefault",
                new { UserId = userId, GridId = gridId },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting default grid settings for User={UserId}, Grid={GridId}", userId, gridId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<UserGridSettings?> GetByIdAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            return await connection.QueryFirstOrDefaultAsync<UserGridSettings>(
                "sp_UserGridSettings_GetById",
                new { Id = id },
                commandType: CommandType.StoredProcedure);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting grid settings by Id={Id}", id);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(CreateUserGridSettingsDto dto)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("Id", null, DbType.Guid, ParameterDirection.Output);
            parameters.Add("UserId", dto.UserId);
            parameters.Add("GridId", dto.GridId);
            parameters.Add("ConfigurationName", dto.ConfigurationName);
            parameters.Add("GridState", dto.GridState);
            parameters.Add("IsDefault", dto.IsDefault);
            parameters.Add("CreatedBy", dto.UserId); // User creates their own settings
            
            await connection.ExecuteAsync(
                "sp_UserGridSettings_Create",
                parameters,
                commandType: CommandType.StoredProcedure);
            
            var id = parameters.Get<Guid>("Id");
            
            _logger.LogInformation("Created grid settings Id={Id} for User={UserId}, Grid={GridId}", 
                id, dto.UserId, dto.GridId);
            
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating grid settings for User={UserId}, Grid={GridId}", 
                dto.UserId, dto.GridId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(UpdateUserGridSettingsDto dto)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            // Get existing configuration to preserve ConfigurationName
            var existing = await GetByIdAsync(dto.Id);
            if (existing == null) return false;
            
            var result = await connection.QueryFirstOrDefaultAsync<int>(
                "sp_UserGridSettings_Update",
                new
                {
                    dto.Id,
                    ConfigurationName = existing.ConfigurationName,
                    dto.GridState,
                    dto.IsDefault,
                    UpdatedBy = (Guid?)null
                },
                commandType: CommandType.StoredProcedure);
            
            return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating grid settings Id={Id}", dto.Id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryFirstOrDefaultAsync<int>(
                "sp_UserGridSettings_Delete",
                new { Id = id, DeletedBy = (Guid?)null },
                commandType: CommandType.StoredProcedure);
            
            return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting grid settings Id={Id}", id);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetAsDefaultAsync(Guid userId, string gridId, Guid configurationId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryFirstOrDefaultAsync<int>(
                "sp_UserGridSettings_SetDefault",
                new { Id = configurationId, UpdatedBy = userId },
                commandType: CommandType.StoredProcedure);
            
            return result > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting default grid settings for User={UserId}, Grid={GridId}", 
                userId, gridId);
            return false;
        }
    }
}
