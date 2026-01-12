using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using ValyanERP.Web.Features.Infrastructure.Security.Models;
using ValyanERP.Web.Infrastructure.Data;
using Dapper;
using System.Data;

namespace ValyanERP.Web.Features.Infrastructure.Security.Services;

/// <summary>
/// Implementation of IWorkingContextService.
/// Stores the working context in browser session storage for persistence across page navigations.
/// </summary>
public class WorkingContextService : IWorkingContextService
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly DapperContext _context;
    private readonly ILogger<WorkingContextService> _logger;
    
    private const string SessionKey = "ValyanERP_WorkingContext";
    
    // Cache the context in memory to avoid repeated session reads
    private WorkingContext? _cachedContext;
    private bool _isInitialized;
    
    public WorkingContextService(
        ProtectedSessionStorage sessionStorage,
        DapperContext context,
        ILogger<WorkingContextService> logger)
    {
        _sessionStorage = sessionStorage;
        _context = context;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public WorkingContext Current => _cachedContext ?? new WorkingContext();
    
    /// <inheritdoc />
    public bool HasContext => _cachedContext?.HasActiveCompany ?? false;
    
    /// <inheritdoc />
    public async Task<bool> SetActiveLocationAsync(Guid locationId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            // Get location with its parent hierarchy
            var location = await connection.QueryFirstOrDefaultAsync<LocationHierarchy>(
                @"SELECT 
                    l.Id AS LocationId,
                    l.Denumire AS LocationName,
                    l.WorkPlaceId,
                    wp.Denumire AS WorkPlaceName,
                    l.CompanyId,
                    c.Denumire AS CompanyName,
                    c.GroupId AS GroupId,
                    g.Denumire AS GroupName
                FROM Locations l
                LEFT JOIN WorkPlaces wp ON l.WorkPlaceId = wp.Id
                INNER JOIN Companies c ON l.CompanyId = c.Id
                LEFT JOIN CompanyGroups g ON c.GroupId = g.Id
                WHERE l.Id = @LocationId AND l.IsActive = 1",
                new { LocationId = locationId });
            
            if (location == null)
            {
                _logger.LogWarning("Location not found or inactive: {LocationId}", locationId);
                return false;
            }
            
            _cachedContext = new WorkingContext
            {
                ActiveLocationId = location.LocationId,
                ActiveLocationName = location.LocationName,
                ActiveWorkPlaceId = location.WorkPlaceId,
                ActiveWorkPlaceName = location.WorkPlaceName,
                ActiveCompanyId = location.CompanyId,
                ActiveCompanyName = location.CompanyName,
                ActiveGroupId = location.GroupId,
                ActiveGroupName = location.GroupName
            };
            
            await SaveContextAsync();
            
            _logger.LogInformation(
                "Working context set to location: {LocationName} ({LocationId}), Company: {CompanyName}",
                location.LocationName, locationId, location.CompanyName);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting active location: {LocationId}", locationId);
            return false;
        }
    }
    
    /// <inheritdoc />
    public async Task<bool> SetActiveWorkPlaceAsync(Guid workPlaceId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            // Get workplace info (without location - user works at WorkPlace level)
            var workPlace = await connection.QueryFirstOrDefaultAsync<WorkPlaceHierarchy>(
                @"SELECT 
                    wp.Id AS WorkPlaceId,
                    wp.Denumire AS WorkPlaceName,
                    wp.CompanyId,
                    c.Denumire AS CompanyName,
                    c.GroupId AS GroupId,
                    g.Denumire AS GroupName
                FROM WorkPlaces wp
                INNER JOIN Companies c ON wp.CompanyId = c.Id
                LEFT JOIN CompanyGroups g ON c.GroupId = g.Id
                WHERE wp.Id = @WorkPlaceId AND wp.IsActive = 1",
                new { WorkPlaceId = workPlaceId });
            
            if (workPlace == null)
            {
                _logger.LogWarning("WorkPlace not found or inactive: {WorkPlaceId}", workPlaceId);
                return false;
            }
            
            _cachedContext = new WorkingContext
            {
                // User works at WorkPlace level - Location stays NULL
                ActiveLocationId = null,
                ActiveLocationName = null,
                ActiveWorkPlaceId = workPlace.WorkPlaceId,
                ActiveWorkPlaceName = workPlace.WorkPlaceName,
                ActiveCompanyId = workPlace.CompanyId,
                ActiveCompanyName = workPlace.CompanyName,
                ActiveGroupId = workPlace.GroupId,
                ActiveGroupName = workPlace.GroupName
            };
            
            await SaveContextAsync();
            
            _logger.LogInformation(
                "Working context set to workplace: {WorkPlaceName} ({WorkPlaceId}), Company: {CompanyName}",
                workPlace.WorkPlaceName, workPlaceId, workPlace.CompanyName);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting active workplace: {WorkPlaceId}", workPlaceId);
            return false;
        }
    }
    
    /// <inheritdoc />
    public async Task<bool> SetActiveCompanyAsync(Guid companyId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var company = await connection.QueryFirstOrDefaultAsync<CompanyHierarchy>(
                @"SELECT 
                    c.Id AS CompanyId,
                    c.Denumire AS CompanyName,
                    c.GroupId AS GroupId,
                    g.Denumire AS GroupName
                FROM Companies c
                LEFT JOIN CompanyGroups g ON c.GroupId = g.Id
                WHERE c.Id = @CompanyId AND c.IsActive = 1",
                new { CompanyId = companyId });
            
            if (company == null)
            {
                _logger.LogWarning("Company not found or inactive: {CompanyId}", companyId);
                return false;
            }
            
            _cachedContext = new WorkingContext
            {
                ActiveLocationId = null,
                ActiveLocationName = null,
                ActiveWorkPlaceId = null,
                ActiveWorkPlaceName = null,
                ActiveCompanyId = company.CompanyId,
                ActiveCompanyName = company.CompanyName,
                ActiveGroupId = company.GroupId,
                ActiveGroupName = company.GroupName
            };
            
            await SaveContextAsync();
            
            _logger.LogInformation(
                "Working context set to company: {CompanyName} ({CompanyId})",
                company.CompanyName, companyId);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting active company: {CompanyId}", companyId);
            return false;
        }
    }
    
    /// <inheritdoc />
    public void Clear()
    {
        _cachedContext = new WorkingContext();
        _isInitialized = true;
        _logger.LogInformation("Working context cleared");
    }
    
    /// <inheritdoc />
    public (Guid? CompanyId, Guid? WorkPlaceId, Guid? LocationId) GetOwnershipContext()
    {
        var ctx = Current;
        return (ctx.ActiveCompanyId, ctx.ActiveWorkPlaceId, ctx.ActiveLocationId);
    }
    
    /// <inheritdoc />
    public async Task<bool> InitializeFromCookieAsync(string? cookieValue)
    {
        if (string.IsNullOrWhiteSpace(cookieValue))
        {
            return false;
        }
        
        // Parse format: "ENTITYTYPE:GUID"
        var parts = cookieValue.Split(':');
        if (parts.Length != 2)
        {
            _logger.LogWarning("Invalid working context cookie format: {CookieValue}", cookieValue);
            return false;
        }
        
        var entityType = parts[0].ToUpperInvariant();
        if (!Guid.TryParse(parts[1], out var entityId))
        {
            _logger.LogWarning("Invalid GUID in working context cookie: {CookieValue}", cookieValue);
            return false;
        }
        
        // Set context based on entity type
        var success = entityType switch
        {
            "LOCATION" => await SetActiveLocationAsync(entityId),
            "WORKPLACE" => await SetActiveWorkPlaceAsync(entityId),
            "COMPANY" => await SetActiveCompanyAsync(entityId),
            _ => false
        };
        
        if (success)
        {
            _logger.LogInformation("Working context initialized from cookie: {EntityType}={EntityId}", entityType, entityId);
        }
        
        return success;
    }
    
    /// <summary>
    /// Loads the context from session storage.
    /// Call this during component initialization.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        
        try
        {
            var result = await _sessionStorage.GetAsync<WorkingContext>(SessionKey);
            if (result.Success && result.Value != null)
            {
                _cachedContext = result.Value;
                _logger.LogDebug("Working context loaded from session: {Context}", _cachedContext.ContextSummary);
            }
            else
            {
                _cachedContext = new WorkingContext();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load working context from session, using empty context");
            _cachedContext = new WorkingContext();
        }
        
        _isInitialized = true;
    }
    
    private async Task SaveContextAsync()
    {
        try
        {
            await _sessionStorage.SetAsync(SessionKey, _cachedContext!);
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save working context to session");
        }
    }
    
    #region Private DTOs for queries
    
    private class LocationHierarchy
    {
        public Guid LocationId { get; set; }
        public string LocationName { get; set; } = string.Empty;
        public Guid? WorkPlaceId { get; set; }
        public string? WorkPlaceName { get; set; }
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public Guid? GroupId { get; set; }
        public string? GroupName { get; set; }
    }
    
    private class WorkPlaceHierarchy
    {
        public Guid WorkPlaceId { get; set; }
        public string WorkPlaceName { get; set; } = string.Empty;
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public Guid? GroupId { get; set; }
        public string? GroupName { get; set; }
        // Primary location associated with this workplace
        public Guid? LocationId { get; set; }
        public string? LocationName { get; set; }
    }
    
    private class CompanyHierarchy
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public Guid? GroupId { get; set; }
        public string? GroupName { get; set; }
    }
    
    #endregion
}
