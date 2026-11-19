using Erp.Core.Application.Abstractions;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Data;
using Erp.Core.Infrastructure.Licensing;
using Erp.Core.Infrastructure.Users;
using Erp.Licensing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Core.Infrastructure;

public static class CoreInfrastructureRegistration
{
    /// <summary>Accesul la date, outbox, audit, idempotenta, licenta si incarcarea utilizatorului (02.01, 4.1 si 6).</summary>
    public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services, string connectionString, LicenseOptions license)
    {
        services.AddScoped(_ => new DbSession(connectionString));
        services.AddScoped<IDbSession>(sp => sp.GetRequiredService<DbSession>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DbSession>());
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IOutbox, OutboxWriter>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IUserContextLoader, UserContextLoader>();
        services.AddScoped<Application.Organization.IOrganizationStore, Organization.OrganizationStore>();
        services.AddScoped<INumberSeriesService, Organization.NumberSeriesService>();
        services.AddSingleton<ITransientFailureDetector, SqlServerTransientFailures>();
        services.AddSingleton<ISqlErrorMap, CoreSqlErrors>();
        services.AddSingleton<ISqlErrorTranslator, SqlErrorTranslator>();
        services.AddSingleton<IPeriodLock, NoClosedPeriods>();
        services.AddSingleton(license);
        services.AddSingleton<ILicenseReader>(sp => new LicenseReader(LicenseKeys.Embedded, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ILicenseInfo, LicenseInfo>();
        return services;
    }
}
