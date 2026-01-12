using ValyanERP.Web.Features.Infrastructure.Security.Models;

namespace ValyanERP.Web.Features.Infrastructure.Security.Services;

/// <summary>
/// Service for managing the user's active working context.
/// The working context determines which organizational entity (location/workplace/company)
/// the user is currently working in, and is used for:
/// - Default ownership when creating new records
/// - Filtering data to show only relevant records
/// - Audit trail context
/// </summary>
public interface IWorkingContextService
{
    /// <summary>
    /// Gets the current working context.
    /// Returns an empty context if none is set.
    /// </summary>
    WorkingContext Current { get; }
    
    /// <summary>
    /// Gets whether a working context is currently set.
    /// </summary>
    bool HasContext { get; }
    
    /// <summary>
    /// Sets the active location and automatically derives Company and WorkPlace from it.
    /// </summary>
    /// <param name="locationId">The location ID to set as active.</param>
    /// <returns>True if the context was set successfully.</returns>
    Task<bool> SetActiveLocationAsync(Guid locationId);
    
    /// <summary>
    /// Sets the active workplace and automatically derives Company from it.
    /// Clears any active location.
    /// </summary>
    /// <param name="workPlaceId">The workplace ID to set as active.</param>
    /// <returns>True if the context was set successfully.</returns>
    Task<bool> SetActiveWorkPlaceAsync(Guid workPlaceId);
    
    /// <summary>
    /// Sets the active company.
    /// Clears any active location and workplace.
    /// </summary>
    /// <param name="companyId">The company ID to set as active.</param>
    /// <returns>True if the context was set successfully.</returns>
    Task<bool> SetActiveCompanyAsync(Guid companyId);
    
    /// <summary>
    /// Clears the current working context.
    /// </summary>
    void Clear();
    
    /// <summary>
    /// Gets the ownership values for creating new records based on current context.
    /// </summary>
    /// <returns>Tuple of (CompanyId, WorkPlaceId, LocationId) for ownership.</returns>
    (Guid? CompanyId, Guid? WorkPlaceId, Guid? LocationId) GetOwnershipContext();
    
    /// <summary>
    /// Initializes the working context from a cookie value.
    /// Format: "ENTITYTYPE:GUID" (e.g., "LOCATION:12345678-1234-1234-1234-123456789abc")
    /// </summary>
    /// <param name="cookieValue">The cookie value in format "TYPE:GUID".</param>
    /// <returns>True if context was initialized successfully.</returns>
    Task<bool> InitializeFromCookieAsync(string? cookieValue);
}
