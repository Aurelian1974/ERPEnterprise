using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using ValyanERP.Web.Infrastructure.Identity;

namespace ValyanERP.Web.Features.Infrastructure.Security.Services;

/// <summary>
/// Provides access to current authenticated user information.
/// Uses AuthenticationStateProvider for Blazor Server compatibility.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<CurrentUserService> _logger;
    
    private ClaimsPrincipal? _cachedPrincipal;
    
    public CurrentUserService(
        AuthenticationStateProvider authenticationStateProvider,
        UserManager<ApplicationUser> userManager,
        ILogger<CurrentUserService> logger)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _userManager = userManager;
        _logger = logger;
    }
    
    /// <inheritdoc />
    public Guid UserId
    {
        get
        {
            var principal = GetPrincipalSync();
            var userIdClaim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
            {
                throw new UnauthorizedAccessException("User is not authenticated or user ID is invalid.");
            }
            
            return userId;
        }
    }
    
    /// <inheritdoc />
    public string? Email
    {
        get
        {
            var principal = GetPrincipalSync();
            return principal?.FindFirst(ClaimTypes.Email)?.Value 
                ?? principal?.FindFirst(ClaimTypes.Name)?.Value;
        }
    }
    
    /// <inheritdoc />
    public bool IsAuthenticated
    {
        get
        {
            var principal = GetPrincipalSync();
            return principal?.Identity?.IsAuthenticated ?? false;
        }
    }
    
    /// <inheritdoc />
    public Guid? GetUserIdOrNull()
    {
        try
        {
            return UserId;
        }
        catch
        {
            return null;
        }
    }
    
    /// <inheritdoc />
    public async Task<bool> IsInRoleAsync(string role)
    {
        var principal = await GetPrincipalAsync();
        
        // First check claims (faster)
        if (principal?.IsInRole(role) == true)
            return true;
        
        // Fallback to UserManager for accuracy
        var userIdStr = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr))
            return false;
        
        var user = await _userManager.FindByIdAsync(userIdStr);
        if (user == null)
            return false;
        
        return await _userManager.IsInRoleAsync(user, role);
    }
    
    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetRolesAsync()
    {
        var principal = await GetPrincipalAsync();
        var userIdStr = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userIdStr))
            return Enumerable.Empty<string>();
        
        var user = await _userManager.FindByIdAsync(userIdStr);
        if (user == null)
            return Enumerable.Empty<string>();
        
        return await _userManager.GetRolesAsync(user);
    }
    
    private async Task<ClaimsPrincipal?> GetPrincipalAsync()
    {
        if (_cachedPrincipal != null)
            return _cachedPrincipal;
        
        var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
        _cachedPrincipal = authState.User;
        return _cachedPrincipal;
    }
    
    private ClaimsPrincipal? GetPrincipalSync()
    {
        // For synchronous access, we need to block on the async call
        // This is not ideal but necessary for property access
        if (_cachedPrincipal != null)
            return _cachedPrincipal;
        
        try
        {
            var authState = _authenticationStateProvider.GetAuthenticationStateAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            _cachedPrincipal = authState.User;
            return _cachedPrincipal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get authentication state synchronously");
            return null;
        }
    }
}
