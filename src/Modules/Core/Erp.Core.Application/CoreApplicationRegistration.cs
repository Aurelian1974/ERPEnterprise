using Erp.Core.Application.Pipeline;
using Erp.Core.Contracts;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.Core.Application;

public static class CoreApplicationRegistration
{
    /// <summary>
    /// MediatR cu pipeline-ul comun in ordinea din 02.01, 4.1: corelare, licenta, autorizare, validare, perioada, tranzactie,
    /// audit (in interiorul tranzactiei, dupa handler). Modulele isi adauga doar assembly-ul handler-elor si validatorilor.
    /// </summary>
    public static IServiceCollection AddErpMediator(this IServiceCollection services, params System.Reflection.Assembly[] moduleAssemblies)
    {
        var assemblies = moduleAssemblies.Prepend(typeof(CoreApplicationRegistration).Assembly).Distinct().ToArray();
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssemblies(assemblies);
            config.AddOpenBehavior(typeof(CorrelationBehavior<,>));
            config.AddOpenBehavior(typeof(LicenseBehavior<,>));
            config.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
            config.AddOpenBehavior(typeof(PeriodLockBehavior<,>));
            config.AddOpenBehavior(typeof(TransactionBehavior<,>));
            config.AddOpenBehavior(typeof(AuditBehavior<,>));
        });
        services.AddValidatorsFromAssemblies(assemblies, ServiceLifetime.Scoped, includeInternalTypes: true);
        services.AddScoped<AuditScope>();
        services.AddScoped<Partners.PartnerSheetListsProvider>();
        services.AddScoped<IAuditScope>(sp => sp.GetRequiredService<AuditScope>());
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}

/// <summary>Permisiunile Nucleului (02.01, 8); aceleasi coduri ca in seed-ul core.Permission.</summary>
public static class CorePermissions
{
    public const string CompanyView = "Core.Company.View";
    public const string CompanyManage = "Core.Company.Manage";
    public const string PartnerView = "Core.Partner.View";
    public const string PartnerManage = "Core.Partner.Manage";
    public const string PartnerViewPersonalData = "Core.Partner.ViewPersonalData";
    public const string PartnerAnonymize = "Core.Partner.Anonymize";
    public const string ItemView = "Core.Item.View";
    public const string ItemManage = "Core.Item.Manage";
    public const string NomenclatureManage = "Core.Nomenclature.Manage";
    public const string DimensionManage = "Core.Dimension.Manage";
    public const string ExchangeRateManageManual = "Core.ExchangeRate.ManageManual";
    public const string NumberSeriesManage = "Core.NumberSeries.Manage";
    public const string PrecisionManage = "Core.Precision.Manage";
    public const string ImportRun = "Core.Import.Run";
    public const string EmployeeView = "Core.Employee.View";
    public const string EmployeeManage = "Core.Employee.Manage";
    public const string UserManage = "Core.User.Manage";
    public const string RoleManage = "Core.Role.Manage";
    public const string AuditView = "Core.Audit.View";
    public const string LicenseManage = "Core.License.Manage";
    public const string SettingsManage = "Core.Settings.Manage";
    public const string PropertyManage = "Core.Property.Manage";
    public const string IntegrationMonitor = "Core.Integration.Monitor";
}
