using Erp.Hosting;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;

// Erp.Worker (8.12): serviciu Windows, procesarea Hangfire pornita doar cu baza compatibila si fara mentenanta (10.4, 10.5).
var builder = Host.CreateApplicationBuilder(args);
var instanceDirectory = builder.Configuration["instance-dir"]
    ?? throw new InvalidOperationException("Argumentul --instance-dir lipsește (8.12).");
builder.Configuration.AddErpInstallation(instanceDirectory);
var instance = builder.Configuration["Erp:Instance"] ?? throw new InvalidOperationException("Erp:Instance lipsește din configurare.");

builder.Services.AddWindowsService(options => options.ServiceName = $"ErpWorker-{instance}");

var build = ErpBuildInfo.From(typeof(Program).Assembly);
var connectionString = InstallationConfiguration.ConnectionString(builder.Configuration, "Worker");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(build);
builder.Services.AddSingleton<ISchemaVersionSource>(new SchemaVersionReader(() => new SqlConnection(connectionString)));
builder.Services.AddSingleton<SchemaVersionGuard>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SchemaVersionGuard>());
builder.Services.AddSingleton(new MaintenanceFlag(builder.Configuration["Erp:MaintenanceFlagPath"], TimeProvider.System));

// Tabelele Hangfire vin din migrarile hangfire/ (8.5), deci PrepareSchemaIfNecessary = false.
GlobalConfiguration.Configuration.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
var storage = new SqlServerStorage(() => new SqlConnection(connectionString), new SqlServerStorageOptions
{
    SchemaName = "hangfire",
    PrepareSchemaIfNecessary = false,
});
builder.Services.AddSingleton<JobStorage>(storage);
builder.Services.AddSingleton<IBackgroundProcessingFactory>(sp => new HangfireProcessingFactory(sp.GetRequiredService<JobStorage>(), instance));
builder.Services.AddHostedService(sp => new BackgroundProcessingLifecycle(
    sp.GetRequiredService<IBackgroundProcessingFactory>(),
    sp.GetRequiredService<SchemaVersionGuard>(),
    sp.GetRequiredService<MaintenanceFlag>(),
    sp.GetRequiredService<ILogger<BackgroundProcessingLifecycle>>(),
    TimeProvider.System));

builder.Build().Run();

internal sealed class HangfireProcessingFactory(JobStorage storage, string instance) : IBackgroundProcessingFactory
{
    public IAsyncDisposable Start() => new Running(new BackgroundJobServer(
        new BackgroundJobServerOptions { ServerName = $"ErpWorker-{instance}:{Environment.MachineName}" }, storage));

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
