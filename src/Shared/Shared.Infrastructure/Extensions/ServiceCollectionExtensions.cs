using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Auth;
using Shared.Infrastructure.Database;
using Shared.Infrastructure.MultiTenancy;
using Shared.Kernel.Abstractions;

namespace Shared.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddSingleton<IDbConnectionFactory>(_ => new DbConnectionFactory(connectionString));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<ITenantContext, TenantContext>();

        return services;
    }
}
