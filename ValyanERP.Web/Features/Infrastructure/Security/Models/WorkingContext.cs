namespace ValyanERP.Web.Features.Infrastructure.Security.Models;

/// <summary>
/// Represents the active working context for a user.
/// User can select which company/location they are currently working in.
/// This affects default ownership for new records.
/// </summary>
public class WorkingContext
{
    /// <summary>
    /// The currently selected group ID.
    /// </summary>
    public Guid? ActiveGroupId { get; set; }
    
    /// <summary>
    /// The currently selected company ID.
    /// </summary>
    public Guid? ActiveCompanyId { get; set; }
    
    /// <summary>
    /// The currently selected workplace ID.
    /// </summary>
    public Guid? ActiveWorkPlaceId { get; set; }
    
    /// <summary>
    /// The currently selected location ID.
    /// </summary>
    public Guid? ActiveLocationId { get; set; }
    
    // =============================================
    // Display Names (for UI)
    // =============================================
    
    /// <summary>
    /// Display name for the active group.
    /// </summary>
    public string? ActiveGroupName { get; set; }
    
    /// <summary>
    /// Display name for the active company.
    /// </summary>
    public string? ActiveCompanyName { get; set; }
    
    /// <summary>
    /// Display name for the active workplace.
    /// </summary>
    public string? ActiveWorkPlaceName { get; set; }
    
    /// <summary>
    /// Display name for the active location.
    /// </summary>
    public string? ActiveLocationName { get; set; }
    
    // =============================================
    // Helper Properties
    // =============================================
    
    /// <summary>
    /// Returns true if a company is selected.
    /// </summary>
    public bool HasActiveCompany => ActiveCompanyId.HasValue;
    
    /// <summary>
    /// Returns true if a location is selected.
    /// </summary>
    public bool HasActiveLocation => ActiveLocationId.HasValue;
    
    /// <summary>
    /// Returns a summary of the current context for display.
    /// Example: "UTI SYSTEMS SA / Sediu Central / Depozit"
    /// </summary>
    public string ContextSummary
    {
        get
        {
            var parts = new List<string>();
            
            if (!string.IsNullOrEmpty(ActiveCompanyName))
                parts.Add(ActiveCompanyName);
            
            if (!string.IsNullOrEmpty(ActiveWorkPlaceName))
                parts.Add(ActiveWorkPlaceName);
            
            if (!string.IsNullOrEmpty(ActiveLocationName))
                parts.Add(ActiveLocationName);
            
            return parts.Count > 0 ? string.Join(" / ", parts) : "Niciun context selectat";
        }
    }
    
    /// <summary>
    /// Clears all context selections.
    /// </summary>
    public void Clear()
    {
        ActiveGroupId = null;
        ActiveCompanyId = null;
        ActiveWorkPlaceId = null;
        ActiveLocationId = null;
        ActiveGroupName = null;
        ActiveCompanyName = null;
        ActiveWorkPlaceName = null;
        ActiveLocationName = null;
    }
}
