using Erp.Hosting;
using Erp.Worker.Jobs;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;

// Erp.Worker (8.12): serviciu Windows, procesarea Hangfire pornita doar cu baza compatibila si fara mentenanta (10.4, 10.5).
// In mediul Development, fara --instance-dir, configurarea vine din appsettings.Development.json / user-secrets.
var builder = Host.CreateApplicationBuilder(args);
var development = builder.Environment.IsDevelopment();
var instanceDirectory = builder.Configuration["instance-dir"];
if (instanceDirectory is not null)
{
    builder.Configuration.AddErpInstallation(instanceDirectory);
}
else if (!development)
{
    throw new InvalidOperationException("Argumentul --instance-dir lipsește (8.12).");
}

var instance = builder.Configuration["Erp:Instance"] ?? throw new InvalidOperationException("Erp:Instance lipsește din configurare.");

builder.Services.AddWindowsService(options => options.ServiceName = $"ErpWorker-{instance}");

var build = ErpBuildInfo.From(typeof(Program).Assembly);
var connectionString = InstallationConfiguration.ConnectionString(builder.Configuration, "Worker");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(build);
builder.Services.AddSingleton<ISchemaVersionSource>(new SchemaVersionReader(() => new SqlConnection(connectionString)));
builder.Services.AddSingleton(sp => new SchemaVersionGuard(sp.GetRequiredService<ISchemaVersionSource>(), build,
    sp.GetRequiredService<ILogger<SchemaVersionGuard>>(), TimeProvider.System, allowUnversionedBuild: development));
builder.Services.AddHostedService(sp => sp.GetRequiredService<SchemaVersionGuard>());
builder.Services.AddSingleton(new MaintenanceFlag(builder.Configuration["Erp:MaintenanceFlagPath"], TimeProvider.System));

// Acelasi inel de chei Data Protection ca Erp.Host (08, 5.12): Worker-ul citeste secretele criptate (de exemplu parola SMTP).
builder.Services.AddErpDataProtection(builder.Configuration, () => new SqlConnection(connectionString), development);

// Tabelele Hangfire vin din migrarile hangfire/ (8.5), deci PrepareSchemaIfNecessary = false.
GlobalConfiguration.Configuration.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
var storage = new SqlServerStorage(() => new SqlConnection(connectionString), new SqlServerStorageOptions
{
    SchemaName = "hangfire",
    PrepareSchemaIfNecessary = false,
});
builder.Services.AddSingleton<JobStorage>(storage);
builder.Services.AddSingleton<Func<SqlConnection>>(() => new SqlConnection(connectionString));
builder.Services.AddTransient<UserAccessExpirationJob>();
builder.Services.AddSingleton<IBackgroundProcessingFactory>(sp => new HangfireProcessingFactory(sp.GetRequiredService<JobStorage>(), instance,
    sp.GetRequiredService<IServiceScopeFactory>()));
builder.Services.AddHostedService(sp => new BackgroundProcessingLifecycle(
    sp.GetRequiredService<IBackgroundProcessingFactory>(),
    sp.GetRequiredService<SchemaVersionGuard>(),
    sp.GetRequiredService<MaintenanceFlag>(),
    sp.GetRequiredService<ILogger<BackgroundProcessingLifecycle>>(),
    TimeProvider.System));

builder.Build().Run();

internal sealed class HangfireProcessingFactory(JobStorage storage, string instance, IServiceScopeFactory scopes) : IBackgroundProcessingFactory
{
    // Joburile recurente se inregistreaza la pornirea procesarii, deci numai cu baza compatibila si fara mentenanta (10.4, 10.5).
    public IAsyncDisposable Start()
    {
        var jobs = new RecurringJobManager(storage);
        // 02.01, 3.4: zilnic, imediat dupa miezul noptii UTC (ziua folosita si de autentificare, core.v_UserLoginStatus).
        jobs.AddOrUpdate<UserAccessExpirationJob>(UserAccessExpirationJob.Id, job => job.RunAsync(CancellationToken.None), "5 0 * * *",
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        return new Running(new BackgroundJobServer(
            new BackgroundJobServerOptions
            {
                ServerName = $"ErpWorker-{instance}:{Environment.MachineName}",
                Activator = new ServiceProviderJobActivator(scopes),
            }, storage));
    }

    private sealed class Running(BackgroundJobServer server) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            server.SendStop();
            server.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
