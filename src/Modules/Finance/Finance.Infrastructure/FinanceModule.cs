using Finance.Application.Abstractions;
using Finance.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure.Database.Migrations;
using Shared.Infrastructure.Abstractions;

namespace Finance.Infrastructure;

public sealed class FinanceModule : IModuleInstaller
{
    public IServiceCollection Install(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IInvoiceReadRepository, InvoiceReadRepository>();

        return services;
    }

    public static void RunMigrations(string connectionString, Microsoft.Extensions.Logging.ILogger logger)
    {
        MigrationRunner.Run(connectionString, [typeof(FinanceModule).Assembly], logger);
    }
}
