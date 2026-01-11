using Microsoft.Extensions.Logging;
using ValyanERP.Web.Features.Infrastructure.GridSettings.Models;
using ValyanERP.Web.Features.Infrastructure.GridSettings.Repositories;

namespace ValyanERP.Web.Features.Infrastructure.GridSettings.Services;

/// <summary>
/// Service for user grid settings business logic.
/// Handles saving, loading, and managing grid configurations.
/// </summary>
public class UserGridSettingsService : IUserGridSettingsService
{
    private readonly IUserGridSettingsRepository _repository;
    private readonly ILogger<UserGridSettingsService> _logger;

    public UserGridSettingsService(
        IUserGridSettingsRepository repository,
        ILogger<UserGridSettingsService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<IEnumerable<UserGridSettings>> GetConfigurationsAsync(Guid userId, string gridId)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(gridId))
        {
            _logger.LogWarning("GetConfigurations called with invalid parameters: UserId={UserId}, GridId={GridId}", 
                userId, gridId);
            return Enumerable.Empty<UserGridSettings>();
        }

        return await _repository.GetByUserAndGridAsync(userId, gridId);
    }

    public async Task<UserGridSettings?> GetDefaultConfigurationAsync(Guid userId, string gridId)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(gridId))
        {
            _logger.LogWarning("GetDefaultConfiguration called with invalid parameters: UserId={UserId}, GridId={GridId}", 
                userId, gridId);
            return null;
        }

        return await _repository.GetDefaultAsync(userId, gridId);
    }

    public async Task<Guid> SaveConfigurationAsync(Guid userId, string gridId, string configurationName, 
        string gridState, bool setAsDefault = false)
    {
        // Validate inputs
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(gridId))
        {
            throw new ArgumentException("GridId is required", nameof(gridId));
        }

        if (string.IsNullOrWhiteSpace(configurationName))
        {
            throw new ArgumentException("ConfigurationName is required", nameof(configurationName));
        }

        try
        {
            // Check if configuration with same name exists
            var existingConfigs = await _repository.GetByUserAndGridAsync(userId, gridId);
            var existing = existingConfigs.FirstOrDefault(c => 
                c.ConfigurationName.Equals(configurationName, StringComparison.OrdinalIgnoreCase));

            Guid configId;

            if (existing != null)
            {
                // Update existing configuration
                await _repository.UpdateAsync(new UpdateUserGridSettingsDto
                {
                    Id = existing.Id,
                    GridState = gridState,
                    IsDefault = setAsDefault
                });
                configId = existing.Id;
                
                _logger.LogInformation("Updated grid configuration: User={UserId}, Grid={GridId}, Config={ConfigName}", 
                    userId, gridId, configurationName);
            }
            else
            {
                // Create new configuration
                configId = await _repository.CreateAsync(new CreateUserGridSettingsDto
                {
                    UserId = userId,
                    GridId = gridId,
                    ConfigurationName = configurationName,
                    GridState = gridState,
                    IsDefault = setAsDefault
                });
                
                _logger.LogInformation("Created grid configuration: User={UserId}, Grid={GridId}, Config={ConfigName}", 
                    userId, gridId, configurationName);
            }

            // Set as default if requested
            if (setAsDefault)
            {
                await _repository.SetAsDefaultAsync(userId, gridId, configId);
            }

            return configId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving grid configuration: User={UserId}, Grid={GridId}, Config={ConfigName}", 
                userId, gridId, configurationName);
            throw;
        }
    }

    public async Task<bool> DeleteConfigurationAsync(Guid configurationId)
    {
        if (configurationId == Guid.Empty)
        {
            throw new ArgumentException("ConfigurationId is required", nameof(configurationId));
        }

        var result = await _repository.DeleteAsync(configurationId);
        
        if (result)
        {
            _logger.LogInformation("Deleted grid configuration: Id={ConfigurationId}", configurationId);
        }
        else
        {
            _logger.LogWarning("Failed to delete grid configuration: Id={ConfigurationId}", configurationId);
        }

        return result;
    }

    public async Task<bool> SetDefaultConfigurationAsync(Guid userId, string gridId, Guid configurationId)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(gridId) || configurationId == Guid.Empty)
        {
            throw new ArgumentException("All parameters are required");
        }

        var result = await _repository.SetAsDefaultAsync(userId, gridId, configurationId);
        
        if (result)
        {
            _logger.LogInformation("Set default grid configuration: User={UserId}, Grid={GridId}, Config={ConfigurationId}", 
                userId, gridId, configurationId);
        }

        return result;
    }
}
