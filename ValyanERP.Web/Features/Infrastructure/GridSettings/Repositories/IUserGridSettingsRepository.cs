using ValyanERP.Web.Features.Infrastructure.GridSettings.Models;

namespace ValyanERP.Web.Features.Infrastructure.GridSettings.Repositories;

/// <summary>
/// Repository interface for user grid settings data access.
/// </summary>
public interface IUserGridSettingsRepository
{
    /// <summary>
    /// Gets all grid configurations for a specific user and grid.
    /// </summary>
    Task<IEnumerable<UserGridSettings>> GetByUserAndGridAsync(Guid userId, string gridId);
    
    /// <summary>
    /// Gets the default configuration for a specific user and grid.
    /// </summary>
    Task<UserGridSettings?> GetDefaultAsync(Guid userId, string gridId);
    
    /// <summary>
    /// Gets a specific configuration by ID.
    /// </summary>
    Task<UserGridSettings?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Creates a new grid configuration.
    /// </summary>
    Task<Guid> CreateAsync(CreateUserGridSettingsDto dto);
    
    /// <summary>
    /// Updates an existing grid configuration.
    /// </summary>
    Task<bool> UpdateAsync(UpdateUserGridSettingsDto dto);
    
    /// <summary>
    /// Deletes a grid configuration.
    /// </summary>
    Task<bool> DeleteAsync(Guid id);
    
    /// <summary>
    /// Sets a configuration as default (and unsets others for same user/grid).
    /// </summary>
    Task<bool> SetAsDefaultAsync(Guid userId, string gridId, Guid configurationId);
}
