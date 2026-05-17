using Administration.Application.Abstractions;
using Administration.Domain.Partners;
using Administration.Infrastructure.Repositories;
using Administration.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Infrastructure.Abstractions;
using Shared.Infrastructure.Database.Migrations;

namespace Administration.Infrastructure;

public sealed class AdministrationModule : IModuleInstaller
{
    public IServiceCollection Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPartnerTypeRepository, PartnerTypeRepository>();
        services.AddScoped<IPartnerRepository, PartnerRepository>();
        services.AddScoped<IPartnerReadRepository, PartnerReadRepository>();
        services.AddScoped<IPartnerSubEntityRepository, PartnerSubEntityRepository>();

        services.AddHttpClient<IAnafService, AnafService>(client =>
        {
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }

    public static void RunMigrations(string connectionString, ILogger logger)
    {
        MigrationRunner.Run(connectionString, [typeof(AdministrationModule).Assembly], logger);
    }
}
