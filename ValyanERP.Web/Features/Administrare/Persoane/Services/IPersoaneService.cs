using ValyanERP.Web.Features.Administrare.Persoane.Models;

namespace ValyanERP.Web.Features.Administrare.Persoane.Services;

/// <summary>
/// Service interface for Persoane business logic.
/// Separates business rules from data access (Repository).
/// </summary>
public interface IPersoaneService
{
    /// <summary>
    /// Validates and creates a new person.
    /// </summary>
    Task<Persoana> CreateAsync(Persoana persoana);

    /// <summary>
    /// Validates and updates an existing person.
    /// </summary>
    Task<bool> UpdateAsync(Persoana persoana);

    /// <summary>
    /// Soft deletes a person (sets IsActive = false).
    /// </summary>
    Task<bool> DeleteAsync(Guid id);

    /// <summary>
    /// Gets a person by ID.
    /// </summary>
    Task<Persoana?> GetByIdAsync(Guid id);

    /// <summary>
    /// Gets a person by email address.
    /// </summary>
    Task<Persoana?> GetByEmailAsync(string email);

    /// <summary>
    /// Gets all active persons (simplified) for dropdowns.
    /// Limited to 1000 records for performance.
    /// </summary>
    Task<IEnumerable<Persoana>> GetAllSimpleAsync();

    /// <summary>
    /// Validates CNP (Romanian Personal Numeric Code).
    /// </summary>
    bool ValidateCNP(string? cnp);
}
