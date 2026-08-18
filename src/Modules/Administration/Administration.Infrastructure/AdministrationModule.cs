using System.Net.Http.Headers;
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

        services.AddMemoryCache();

        services.AddHttpClient<IAnafService, AnafService>(client =>
        {
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        var localitatiApiKey = configuration["Localitati:ApiKey"]
            ?? throw new InvalidOperationException("Localitati:ApiKey is not configured.");

        services.AddHttpClient<ILocalitatiService, LocalitatiService>(client =>
        {
            client.BaseAddress = new Uri("https://api.localitati.dev/v1/");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.Add("X-API-Key", localitatiApiKey);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient<INominatimService, NominatimService>(client =>
        {
            client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ERPEnterprise", "1.0"));
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+admin@erp.local)"));
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }

    public static void RunMigrations(string connectionString, ILogger logger)
    {
        MigrationRunner.Run(connectionString, [typeof(AdministrationModule).Assembly], logger);
    }
}
