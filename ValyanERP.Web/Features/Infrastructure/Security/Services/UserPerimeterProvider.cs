using System.Data;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using ValyanERP.Web.Features.Infrastructure.Security.Data;
using ValyanERP.Web.Features.Infrastructure.Security.Models;

namespace ValyanERP.Web.Features.Infrastructure.Security.Services;

/// <summary>
/// Implementation of IUserPerimeterProvider.
/// Loads user permissions from database and caches them in memory.
/// </summary>
public class UserPerimeterProvider : IUserPerimeterProvider
{
    private readonly ISecureConnectionFactory _connectionFactory;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserPerimeterProvider> _logger;
    
    /// <summary>
    /// Cache duration for user perimeter (30 minutes).
    /// </summary>
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    
    public UserPerimeterProvider(
        ISecureConnectionFactory connectionFactory,
        ICurrentUserService currentUserService,
        IMemoryCache cache,
        ILogger<UserPerimeterProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _currentUserService = currentUserService;
        _cache = cache;
        _logger = logger;
    }
    
    private string GetCacheKey(Guid userId) => $"UserPerimeter_{userId}";
    
    /// <inheritdoc />
    public async Task<UserPerimeter> GetPerimeterAsync()
    {
        var userId = _currentUserService.GetUserIdOrNull();
        if (!userId.HasValue)
        {
            return new UserPerimeter { HasFullAccess = false };
        }
        
        var cacheKey = GetCacheKey(userId.Value);
        
        if (_cache.TryGetValue(cacheKey, out UserPerimeter? cached) && cached != null && !cached.IsExpired)
        {
            return cached;
        }
        
        var perimeter = await LoadPerimeterFromDatabaseAsync(userId.Value);
        
        _cache.Set(cacheKey, perimeter, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1 // Count as 1 entry for memory cache size limit
        });
        
        _logger.LogInformation(
            "Loaded perimeter for user {UserId}: {CompanyCount} companies, {LocationCount} locations, FullAccess: {FullAccess}",
            userId.Value, perimeter.VisibleCompanyIds.Count, perimeter.VisibleLocationIds.Count, perimeter.HasFullAccess);
        
        return perimeter;
    }
    
    private async Task<UserPerimeter> LoadPerimeterFromDatabaseAsync(Guid userId)
    {
        var perimeter = new UserPerimeter
        {
            UserId = userId,
            CachedAt = DateTime.UtcNow
        };
        
        // Check if user is admin
        perimeter.HasFullAccess = await _currentUserService.IsInRoleAsync("Admin");
        
        if (perimeter.HasFullAccess)
        {
            // Admin sees everything - no need to load specific permissions
            _logger.LogDebug("User {UserId} is admin, granting full access", userId);
            return perimeter;
        }
        
        using var db = _connectionFactory.CreateSystemConnection();
        
        // Load visible companies with access levels
        var companies = await db.QueryAsync<(Guid CompanyId, byte AccessLevel)>(
            "SELECT CompanyId, AccessLevel FROM dbo.fn_GetUserVisibleCompanies(@UserId)",
            new { UserId = userId });
        
        foreach (var (companyId, accessLevel) in companies)
        {
            perimeter.VisibleCompanyIds.Add(companyId);
            perimeter.CompanyAccessLevels[companyId] = accessLevel;
        }
        
        // Load visible locations with access levels
        var locations = await db.QueryAsync<(Guid LocationId, byte AccessLevel)>(
            "SELECT LocationId, AccessLevel FROM dbo.fn_GetUserVisibleLocations(@UserId)",
            new { UserId = userId });
        
        foreach (var (locationId, accessLevel) in locations)
        {
            perimeter.VisibleLocationIds.Add(locationId);
            perimeter.LocationAccessLevels[locationId] = accessLevel;
        }
        
        // Load visible workplaces with access levels
        var workplaces = await db.QueryAsync<(Guid WorkPlaceId, byte AccessLevel)>(
            "SELECT WorkPlaceId, AccessLevel FROM dbo.fn_GetUserVisibleWorkPlaces(@UserId)",
            new { UserId = userId });
        
        foreach (var (workPlaceId, accessLevel) in workplaces)
        {
            perimeter.VisibleWorkPlaceIds.Add(workPlaceId);
            perimeter.WorkPlaceAccessLevels[workPlaceId] = accessLevel;
        }
        
        // Load visible groups with access levels
        var groups = await db.QueryAsync<(Guid GroupId, byte AccessLevel)>(
            "SELECT GroupId, AccessLevel FROM dbo.fn_GetUserVisibleGroups(@UserId)",
            new { UserId = userId });
        
        foreach (var (groupId, accessLevel) in groups)
        {
            perimeter.VisibleGroupIds.Add(groupId);
            perimeter.GroupAccessLevels[groupId] = accessLevel;
        }
        
        return perimeter;
    }
    
    /// <inheritdoc />
    public async Task<IEnumerable<Guid>> GetVisibleCompanyIdsAsync()
    {
        var perimeter = await GetPerimeterAsync();
        
        if (perimeter.HasFullAccess)
        {
            using var db = _connectionFactory.CreateSystemConnection();
            return await db.QueryAsync<Guid>("SELECT Id FROM Companies WHERE IsActive = 1");
        }
        
        return perimeter.VisibleCompanyIds;
    }
    
    /// <inheritdoc />
    public async Task<IEnumerable<Guid>> GetVisibleLocationIdsAsync()
    {
        var perimeter = await GetPerimeterAsync();
        
        if (perimeter.HasFullAccess)
        {
            using var db = _connectionFactory.CreateSystemConnection();
            return await db.QueryAsync<Guid>("SELECT Id FROM Locations WHERE IsActive = 1");
        }
        
        return perimeter.VisibleLocationIds;
    }
    
    /// <inheritdoc />
    public async Task<IEnumerable<Guid>> GetVisibleWorkPlaceIdsAsync()
    {
        var perimeter = await GetPerimeterAsync();
        
        if (perimeter.HasFullAccess)
        {
            using var db = _connectionFactory.CreateSystemConnection();
            return await db.QueryAsync<Guid>("SELECT Id FROM WorkPlaces WHERE IsActive = 1");
        }
        
        return perimeter.VisibleWorkPlaceIds;
    }
    
    /// <inheritdoc />
    public async Task<IEnumerable<Guid>> GetVisibleGroupIdsAsync()
    {
        var perimeter = await GetPerimeterAsync();
        
        if (perimeter.HasFullAccess)
        {
            using var db = _connectionFactory.CreateSystemConnection();
            return await db.QueryAsync<Guid>("SELECT Id FROM CompanyGroups WHERE IsActive = 1");
        }
        
        return perimeter.VisibleGroupIds;
    }
    
    /// <inheritdoc />
    public async Task<bool> CanAccessAsync(string entityType, Guid entityId, byte requiredLevel = 1)
    {
        var perimeter = await GetPerimeterAsync();
        
        if (perimeter.HasFullAccess)
            return true;
        
        var actualLevel = perimeter.GetAccessLevel(entityType, entityId);
        return actualLevel >= requiredLevel;
    }
    
    /// <inheritdoc />
    public async Task<bool> CanWriteToCompanyAsync(Guid companyId)
    {
        var perimeter = await GetPerimeterAsync();
        return perimeter.CanWriteToCompany(companyId);
    }
    
    /// <inheritdoc />
    public async Task<bool> CanWriteToLocationAsync(Guid locationId)
    {
        var perimeter = await GetPerimeterAsync();
        return perimeter.CanWriteToLocation(locationId);
    }
    
    /// <inheritdoc />
    public Task InvalidateCacheAsync()
    {
        var userId = _currentUserService.GetUserIdOrNull();
        if (userId.HasValue)
        {
            _cache.Remove(GetCacheKey(userId.Value));
            _logger.LogInformation("Perimeter cache invalidated for current user {UserId}", userId.Value);
        }
        return Task.CompletedTask;
    }
    
    /// <inheritdoc />
    public Task InvalidateCacheForUserAsync(Guid userId)
    {
        _cache.Remove(GetCacheKey(userId));
        _logger.LogInformation("Perimeter cache invalidated for user {UserId}", userId);
        return Task.CompletedTask;
    }
}
