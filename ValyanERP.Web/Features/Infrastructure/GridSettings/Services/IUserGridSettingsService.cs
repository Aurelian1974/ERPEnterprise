using ValyanERP.Web.Features.Infrastructure.GridSettings.Models;

namespace ValyanERP.Web.Features.Infrastructure.GridSettings.Services;

/// <summary>
/// Service interface for user grid settings business logic.
/// </summary>
public interface IUserGridSettingsService
{
    /// <summary>
    /// Gets all grid configurations for a specific user and grid.
    /// </summary>
    Task<IEnumerable<UserGridSettings>> GetConfigurationsAsync(Guid userId, string gridId);
    
    /// <summary>
    /// Gets the default (or first available) configuration for a user/grid.
    /// </summary>
    Task<UserGridSettings?> GetDefaultConfigurationAsync(Guid userId, string gridId);
    
    /// <summary>
    /// Saves a grid configuration for the current user.
    /// Creates new or updates existing based on configuration name.
    /// </summary>
    Task<Guid> SaveConfigurationAsync(Guid userId, string gridId, string configurationName, 
        string gridState, bool setAsDefault = false);
    
    /// <summary>
    /// Deletes a grid configuration.
    /// </summary>
    Task<bool> DeleteConfigurationAsync(Guid configurationId);
    
    /// <summary>
    /// Sets a configuration as the default for this user/grid.
    /// </summary>
    Task<bool> SetDefaultConfigurationAsync(Guid userId, string gridId, Guid configurationId);
}
