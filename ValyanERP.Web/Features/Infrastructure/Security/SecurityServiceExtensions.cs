using ValyanERP.Web.Features.Infrastructure.Security.Data;
using ValyanERP.Web.Features.Infrastructure.Security.Repositories;
using ValyanERP.Web.Features.Infrastructure.Security.Services;

namespace ValyanERP.Web.Features.Infrastructure.Security;

/// <summary>
/// Extension methods for registering organizational security services.
/// </summary>
public static class SecurityServiceExtensions
{
    /// <summary>
    /// Adds organizational security services to the service collection.
    /// Includes:
    /// - ICurrentUserService: Access to current authenticated user
    /// - ISecureConnectionFactory: Database connections with SESSION_CONTEXT
    /// - IUserPerimeterProvider: User permission perimeter with caching
    /// - IUserOrganizationalAccessRepository: CRUD for user permissions
    /// - IWorkingContextService: User's active working context (selected entity)
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOrganizationalSecurity(this IServiceCollection services)
    {
        // Current user service (scoped - per request)
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        
        // Secure connection factory (scoped - uses current user)
        services.AddScoped<ISecureConnectionFactory, SecureConnectionFactory>();
        
        // User perimeter provider (scoped - caches per request, uses IMemoryCache for cross-request)
        services.AddScoped<IUserPerimeterProvider, UserPerimeterProvider>();
        
        // User organizational access repository (scoped - CRUD for permissions)
        services.AddScoped<IUserOrganizationalAccessRepository, UserOrganizationalAccessRepository>();
        
        // Working context service (scoped - user's selected entity for ownership)
        services.AddScoped<IWorkingContextService, WorkingContextService>();
        
        return services;
    }
}
