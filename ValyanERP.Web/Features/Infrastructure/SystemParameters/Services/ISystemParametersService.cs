// ================================================================
// FILE: ISystemParametersService.cs
// PURPOSE: Service interface for system parameters business logic
// ARCHITECTURE: Service Layer with caching for performance
// ================================================================

using ValyanERP.Web.Features.Infrastructure.SystemParameters.Models;

namespace ValyanERP.Web.Features.Infrastructure.SystemParameters.Services;

/// <summary>
/// Service for managing system configuration parameters with caching.
/// </summary>
/// <remarks>
/// This service provides:
/// - Type-safe parameter accessors (GetInt, GetString, GetBool, etc.)
/// - Automatic caching with manual invalidation on updates
/// - Default value fallback when parameter not found or invalid
/// - Business logic and validation
/// </remarks>
public interface ISystemParametersService
{
    // ================================================================
    // Type-Safe Parameter Accessors
    // ================================================================

    /// <summary>
    /// Gets an integer parameter value.
    /// </summary>
    /// <param name="parameterKey">Parameter key (e.g., "Cache.Persoane.DurationMinutes").</param>
    /// <param name="defaultValue">Default value if parameter not found or invalid.</param>
    /// <returns>Parameter value as int, or default if not found/invalid.</returns>
    Task<int> GetIntAsync(string parameterKey, int defaultValue = 0);

    /// <summary>
    /// Gets a string parameter value.
    /// </summary>
    /// <param name="parameterKey">Parameter key (e.g., "Business.DefaultCountry").</param>
    /// <param name="defaultValue">Default value if parameter not found or empty.</param>
    /// <returns>Parameter value as string, or default if not found.</returns>
    Task<string> GetStringAsync(string parameterKey, string defaultValue = "");

    /// <summary>
    /// Gets a boolean parameter value.
    /// </summary>
    /// <param name="parameterKey">Parameter key (e.g., "Feature.EnableNewUI").</param>
    /// <param name="defaultValue">Default value if parameter not found or invalid.</param>
    /// <returns>Parameter value as bool, or default if not found/invalid.</returns>
    Task<bool> GetBoolAsync(string parameterKey, bool defaultValue = false);

    /// <summary>
    /// Gets a decimal parameter value.
    /// </summary>
    /// <param name="parameterKey">Parameter key (e.g., "Business.VATRate").</param>
    /// <param name="defaultValue">Default value if parameter not found or invalid.</param>
    /// <returns>Parameter value as decimal, or default if not found/invalid.</returns>
    Task<decimal> GetDecimalAsync(string parameterKey, decimal defaultValue = 0m);

    /// <summary>
    /// Gets a JSON parameter value and deserializes it.
    /// </summary>
    /// <typeparam name="T">Type to deserialize JSON into.</typeparam>
    /// <param name="parameterKey">Parameter key (e.g., "Enum.DataType.Values").</param>
    /// <param name="defaultValue">Default value if parameter not found or invalid JSON.</param>
    /// <returns>Deserialized object, or default if not found/invalid.</returns>
    Task<T?> GetJsonAsync<T>(string parameterKey, T? defaultValue = default);

    // ================================================================
    // CRUD Operations (Admin UI)
    // ================================================================

    /// <summary>
    /// Gets all parameters with optional filtering.
    /// </summary>
    /// <param name="category">Optional category filter.</param>
    /// <param name="subCategory">Optional sub-category filter.</param>
    /// <param name="includeReadOnly">Include read-only parameters.</param>
    /// <returns>Collection of system parameters.</returns>
    Task<IEnumerable<SystemParameter>> GetAllAsync(
        string? category = null,
        string? subCategory = null,
        bool includeReadOnly = true);

    /// <summary>
    /// Gets all parameters for a specific category.
    /// </summary>
    /// <param name="category">Category name (e.g., "Cache", "Validation").</param>
    /// <returns>All parameters in category.</returns>
    Task<IEnumerable<SystemParameter>> GetByCategoryAsync(string category);

    /// <summary>
    /// Creates a new system parameter.
    /// </summary>
    /// <param name="parameter">Parameter to create.</param>
    /// <param name="createdBy">Optional user ID who created this parameter.</param>
    /// <returns>ID of newly created parameter.</returns>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    /// <remarks>
    /// Invalidates cache after successful creation.
    /// </remarks>
    Task<Guid> CreateAsync(SystemParameter parameter, Guid? createdBy = null);

    /// <summary>
    /// Updates an existing system parameter.
    /// </summary>
    /// <param name="parameter">Parameter with updated values.</param>
    /// <param name="updatedBy">Optional user ID who modified this parameter.</param>
    /// <returns>True if updated successfully.</returns>
    /// <exception cref="ArgumentException">Thrown when validation fails.</exception>
    /// <remarks>
    /// Invalidates cache after successful update.
    /// </remarks>
    Task<bool> UpdateAsync(SystemParameter parameter, Guid? updatedBy = null);

    /// <summary>
    /// Soft deletes a system parameter.
    /// </summary>
    /// <param name="id">ID of parameter to delete.</param>
    /// <param name="updatedBy">Optional user ID who deleted this parameter.</param>
    /// <returns>True if deleted successfully.</returns>
    /// <remarks>
    /// Invalidates cache after successful deletion.
    /// </remarks>
    Task<bool> DeleteAsync(Guid id, Guid? updatedBy = null);

    /// <summary>
    /// Gets distinct categories with parameter counts.
    /// </summary>
    /// <returns>List of categories with counts.</returns>
    Task<IEnumerable<(string Category, int ParameterCount)>> GetCategoriesAsync();

    // ================================================================
    // Cache Management
    // ================================================================

    /// <summary>
    /// Clears all cached parameters.
    /// </summary>
    /// <remarks>
    /// Called automatically after CUD operations.
    /// Can be called manually if parameters changed externally.
    /// </remarks>
    void ClearCache();

    /// <summary>
    /// Event that is triggered when a system parameter is changed or cache is cleared.
    /// ParameterKey will be null when a bulk/clear operation occurs.
    /// </summary>
    event EventHandler<SystemParameterChangedEventArgs>? ParameterChanged;
}
