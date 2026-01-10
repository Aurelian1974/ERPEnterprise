using Syncfusion.Blazor;
using Syncfusion.Blazor.Data;
using ValyanERP.Web.Features.Administrare.Persoane.Models;

namespace ValyanERP.Web.Features.Administrare.Persoane.Repositories;

/// <summary>
/// Repository interface for Persoane data access operations.
/// </summary>
/// <remarks>
/// All operations use stored procedures for SQL injection prevention,
/// performance optimization, and business rule enforcement.
/// Implements soft delete pattern - records are never physically deleted.
/// </remarks>
public interface IPersoaneRepository
{
    /// <summary>
    /// Retrieves paged data for Syncfusion DataGrid with filtering, sorting, and grouping.
    /// </summary>
    /// <param name="dm">DataManager request with paging, filtering, sorting parameters.</param>
    /// <returns>DataResult containing records and total count.</returns>
    Task<DataResult> GetPagedAsync(DataManagerRequest dm);
    
    /// <summary>
    /// Retrieves a single person by unique identifier.
    /// </summary>
    /// <param name="id">Person's unique GUID identifier.</param>
    /// <returns>Person object if found, null otherwise.</returns>
    Task<Persoana?> GetByIdAsync(Guid id);
    
    /// <summary>
    /// Creates a new person record.
    /// </summary>
    /// <param name="persoana">Person object to create.</param>
    /// <exception cref="InvalidOperationException">Thrown when CNP or Email already exists.</exception>
    Task CreateAsync(Persoana persoana);
    
    /// <summary>
    /// Updates an existing person record.
    /// </summary>
    /// <param name="persoana">Person object with updated values.</param>
    /// <exception cref="InvalidOperationException">Thrown when person not found.</exception>
    Task UpdateAsync(Persoana persoana);
    
    /// <summary>
    /// Soft deletes a person record (sets IsActive = false).
    /// </summary>
    /// <param name="id">Person's unique identifier.</param>
    /// <remarks>Soft delete preserves data integrity and audit trail.</remarks>
    Task DeleteAsync(Guid id);
    
    /// <summary>
    /// Retrieves a person by their email address.
    /// </summary>
    /// <param name="email">Email address to search for.</param>
    /// <returns>Person object if found, null otherwise.</returns>
    Task<Persoana?> GetByEmailAsync(string email);
    
    /// <summary>
    /// Retrieves simplified person data for dropdown lists.
    /// </summary>
    /// <returns>Collection of persons with only essential fields (Id, Nume, Prenume).</returns>
    /// <remarks>Optimized query returning minimal data for UI dropdowns. Results are cached.</remarks>
    Task<IEnumerable<Persoana>> GetAllSimpleAsync();
}
