// ================================================================
// FILE: ISystemParametersRepository.cs
// PURPOSE: Repository interface for system parameters data access
// ARCHITECTURE: Repository Pattern - abstraction over data layer
// ================================================================

using ValyanERP.Web.Features.Infrastructure.SystemParameters.Models;

namespace ValyanERP.Web.Features.Infrastructure.SystemParameters.Repositories;

/// <summary>
/// Repository interface for managing system configuration parameters.
/// </summary>
/// <remarks>
/// All operations use stored procedures for:
/// - SQL injection prevention
/// - Performance optimization
/// - Business rule enforcement
/// </remarks>
public interface ISystemParametersRepository
{
    /// <summary>
    /// Retrieves all active system parameters with optional filtering.
    /// </summary>
    /// <param name="category">Optional category filter (e.g., "Cache", "Validation").</param>
    /// <param name="subCategory">Optional sub-category filter (e.g., "Persoane").</param>
    /// <param name="includeReadOnly">Include read-only parameters in results (default: true).</param>
    /// <returns>Collection of system parameters matching filters.</returns>
    Task<IEnumerable<SystemParameter>> GetAllAsync(
        string? category = null, 
        string? subCategory = null, 
        bool includeReadOnly = true);

    /// <summary>
    /// Retrieves a single parameter by its unique key.
    /// </summary>
    /// <param name="parameterKey">Unique parameter key (e.g., "Cache.Persoane.DurationMinutes").</param>
    /// <returns>System parameter if found, null otherwise.</returns>
    /// <remarks>
    /// This is the primary method for reading configuration values.
    /// </remarks>
    Task<SystemParameter?> GetByKeyAsync(string parameterKey);

    /// <summary>
    /// Retrieves all parameters for a specific category.
    /// </summary>
    /// <param name="category">Category name (e.g., "Cache", "Validation", "Business").</param>
    /// <returns>All parameters in the specified category.</returns>
    /// <remarks>
    /// Useful for loading all cache settings, all validation rules, etc.
    /// </remarks>
    Task<IEnumerable<SystemParameter>> GetByCategoryAsync(string category);

    /// <summary>
    /// Creates a new system parameter.
    /// </summary>
    /// <param name="parameter">Parameter to create (Id will be auto-generated if not provided).</param>
    /// <param name="createdBy">Optional user ID who created this parameter.</param>
    /// <returns>ID of newly created parameter.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when:
    /// - ParameterKey already exists
    /// - DataType is invalid
    /// - Validation rules are violated
    /// </exception>
    Task<Guid> CreateAsync(SystemParameter parameter, Guid? createdBy = null);

    /// <summary>
    /// Updates an existing system parameter.
    /// </summary>
    /// <param name="parameter">Parameter with updated values.</param>
    /// <param name="updatedBy">Optional user ID who modified this parameter.</param>
    /// <returns>True if parameter was updated, false if not found or read-only.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when:
    /// - Parameter is read-only
    /// - Parameter not found
    /// - Validation rules are violated
    /// </exception>
    /// <remarks>
    /// ParameterKey cannot be changed (immutable identifier).
    /// </remarks>
    Task<bool> UpdateAsync(SystemParameter parameter, Guid? updatedBy = null);

    /// <summary>
    /// Soft deletes a system parameter (sets IsActive = false).
    /// </summary>
    /// <param name="id">ID of parameter to delete.</param>
    /// <param name="updatedBy">Optional user ID who deleted this parameter.</param>
    /// <returns>True if parameter was deleted, false if not found or read-only.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when parameter is read-only (cannot be deleted).
    /// </exception>
    /// <remarks>
    /// Soft delete preserves audit trail. Hard delete is never performed.
    /// </remarks>
    Task<bool> DeleteAsync(Guid id, Guid? updatedBy = null);

    /// <summary>
    /// Gets distinct categories with parameter counts.
    /// </summary>
    /// <returns>List of categories with how many parameters each contains.</returns>
    /// <remarks>
    /// Used for filtering/grouping in admin UI.
    /// </remarks>
    Task<IEnumerable<(string Category, int ParameterCount)>> GetCategoriesAsync();
}
