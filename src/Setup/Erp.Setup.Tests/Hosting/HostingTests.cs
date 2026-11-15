using System.Net;
using System.Text.Json;
using Erp.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Erp.Setup.Tests.Hosting;

internal sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2027, 1, 20, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FixedSchema(string? value) : ISchemaVersionSource
{
    public string? Value { get; set; } = value;

    public Task<string?> ReadAsync(CancellationToken ct) => Task.FromResult(Value);
}

internal sealed class FixedCheck(string name, HealthStatus status) : IReadinessCheck
{
    public string Name => name;

    public Task<HealthStatus> CheckAsync(CancellationToken ct) => Task.FromResult(status);
}

public sealed class HostingTests : IDisposable
{
    private static readonly ErpBuildInfo Build = new("2027.1.0", "core/2027.01.004_Add_User_IsSystem.sql");
    private readonly string folder = Path.Combine(Path.GetTempPath(), "erp-host-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTime time = new();

    public HostingTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string FlagPath => Path.Combine(folder, "maintenance.flag");

    private async Task<IHost> StartAsync(IReadOnlyList<IReadinessCheck> checks, SchemaVersionGuard? guard = null)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(new MaintenanceFlag(FlagPath, time));
                    services.AddSingleton(guard ?? Guard(Build.LastMigration));
                })
                .Configure(app =>
                {
                    app.UseMiddleware<MaintenanceMiddleware>();
                    app.UseMiddleware<SchemaGuardMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapErpHealth(Build, () => checks);
                        endpoints.MapGet("/api/ping", () => "pong");
                        endpoints.MapGet("/", () => Results.Content("<html></html>", "text/html"));
                    });
                }))
            .Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private SchemaVersionGuard Guard(string? actual)
    {
        var guard = new SchemaVersionGuard(new FixedSchema(actual), Build, NullLogger<SchemaVersionGuard>.Instance, time);
        guard.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
        return guard;
    }

    private static Task<HttpContext> Send(IHost host, string path, IPAddress remote) =>
        host.GetTestServer().SendAsync(context =>
        {
            context.Request.Path = path;
            context.Connection.RemoteIpAddress = remote;
        });

    private static async Task<string> Body(HttpContext context) =>
        await new StreamReader(context.Response.Body).ReadToEndAsync();

    private static IReadOnlyList<IReadinessCheck> AllHealthy() =>
        [new FixedCheck("database", HealthStatus.Healthy), new FixedCheck("schema", HealthStatus.Healthy),
         new FixedCheck("license", HealthStatus.Healthy), new FixedCheck("worker", HealthStatus.Healthy)];

    [Fact]
    public async Task Maintenance_flag_returns_503_with_retry_after_and_page_except_loopback_health()
    {
        using var host = await StartAsync(AllHealthy());
        File.WriteAllText(FlagPath, string.Empty);

        var page = await Send(host, "/", IPAddress.Parse("10.0.0.5"));
        var api = await Send(host, "/api/ping", IPAddress.Loopback);
        var remoteHealth = await Send(host, "/health/ready", IPAddress.Parse("10.0.0.5"));
        var localHealth = await Send(host, "/health/ready", IPAddress.Loopback);
        var localIpv6 = await Send(host, "/health/live", IPAddress.IPv6Loopback);

        Assert.Equal(503, page.Response.StatusCode);
        Assert.Equal("300", page.Response.Headers.RetryAfter.ToString());
        Assert.Contains("Aplicația se actualizează.", await Body(page));
        Assert.Equal(503, api.Response.StatusCode);
        Assert.Equal(503, remoteHealth.Response.StatusCode);
        Assert.Equal(200, localHealth.Response.StatusCode);
        Assert.Equal(200, localIpv6.Response.StatusCode);
    }

    [Fact]
    public async Task Maintenance_flag_is_checked_at_most_every_two_seconds()
    {
        using var host = await StartAsync(AllHealthy());
        Assert.Equal(200, (await Send(host, "/", IPAddress.Parse("10.0.0.5"))).Response.StatusCode);

        File.WriteAllText(FlagPath, string.Empty);
        Assert.Equal(200, (await Send(host, "/", IPAddress.Parse("10.0.0.5"))).Response.StatusCode);

        time.Now += MaintenanceFlag.CheckInterval;
        Assert.Equal(503, (await Send(host, "/", IPAddress.Parse("10.0.0.5"))).Response.StatusCode);

        File.Delete(FlagPath);
        time.Now += MaintenanceFlag.CheckInterval;
        Assert.Equal(200, (await Send(host, "/", IPAddress.Parse("10.0.0.5"))).Response.StatusCode);
    }

    [Fact]
    public async Task Ready_on_loopback_returns_the_installer_contract()
    {
        using var host = await StartAsync(AllHealthy());

        var context = await Send(host, "/health/ready", IPAddress.Loopback);

        Assert.Equal(200, context.Response.StatusCode);
        using var json = JsonDocument.Parse(await Body(context));
        var root = json.RootElement;
        Assert.Equal(["status", "version", "schemaVersion", "checks"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal("2027.1.0", root.GetProperty("version").GetString());
        Assert.Equal(["database", "schema", "license", "worker"], root.GetProperty("checks").EnumerateObject().Select(p => p.Name));
        Assert.All(root.GetProperty("checks").EnumerateObject(), p => Assert.Equal("Healthy", p.Value.GetString()));
    }

    [Fact]
    public async Task Ready_from_the_network_returns_only_the_status_and_503_when_unhealthy()
    {
        using var host = await StartAsync([new FixedCheck("database", HealthStatus.Healthy), new FixedCheck("worker", HealthStatus.Unhealthy)]);

        var remote = await Send(host, "/health/ready", IPAddress.Parse("10.0.0.5"));
        var local = await Send(host, "/health/ready", IPAddress.Loopback);

        Assert.Equal(503, remote.Response.StatusCode);
        Assert.Equal("""{"status":"Unhealthy"}""", await Body(remote));
        Assert.Contains("\"worker\":\"Unhealthy\"", await Body(local));
        Assert.Equal(200, (await Send(host, "/health/live", IPAddress.Parse("10.0.0.5"))).Response.StatusCode);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-18")]
    public async Task Business_api_is_refused_when_the_database_version_does_not_match()
    {
        using var host = await StartAsync(AllHealthy(), Guard("core/2026.11.003_Insert_SystemUser.sql"));

        var api = await Send(host, "/api/ping", IPAddress.Loopback);
        var page = await Send(host, "/", IPAddress.Loopback);

        Assert.Equal(503, api.Response.StatusCode);
        Assert.Contains("CORE_SCHEMA_VERSION_MISMATCH", await Body(api));
        Assert.Equal(200, page.Response.StatusCode);
    }

    [Theory]
    [Trait("Criteriu", "08-IA-18")]
    [InlineData("core/2027.01.004_X.sql", "core/2027.01.004_X.sql", SchemaCompatibility.Compatible)]
    [InlineData("core/2027.01.004_X.sql", "core/2027.01.003_X.sql", SchemaCompatibility.Incompatible)]
    [InlineData("core/2027.01.004_X.sql", "CORE/2027.01.004_X.sql", SchemaCompatibility.Incompatible)]
    [InlineData("core/2027.01.004_X.sql", null, SchemaCompatibility.Incompatible)]
    [InlineData(null, "core/2027.01.004_X.sql", SchemaCompatibility.Incompatible)]
    public void Schema_version_must_match_exactly(string? expected, string? actual, SchemaCompatibility result)
    {
        Assert.Equal(result, SchemaVersionGuard.Evaluate(expected, actual));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-18")]
    public async Task Worker_processing_runs_only_with_a_compatible_database_and_without_maintenance()
    {
        var factory = new CountingFactory();
        var guard = Guard(Build.LastMigration);
        var lifecycle = new BackgroundProcessingLifecycle(factory, guard, new MaintenanceFlag(FlagPath, time),
            NullLogger<BackgroundProcessingLifecycle>.Instance, time);

        await lifecycle.ReconcileAsync();
        Assert.True(lifecycle.IsRunning);

        File.WriteAllText(FlagPath, string.Empty);
        time.Now += MaintenanceFlag.CheckInterval;
        await lifecycle.ReconcileAsync();
        Assert.False(lifecycle.IsRunning);

        File.Delete(FlagPath);
        time.Now += MaintenanceFlag.CheckInterval;
        await lifecycle.ReconcileAsync();
        Assert.Equal((2, 1), (factory.Started, factory.Stopped));

        var incompatible = new BackgroundProcessingLifecycle(new CountingFactory(), Guard("alta"), new MaintenanceFlag(null, time),
            NullLogger<BackgroundProcessingLifecycle>.Instance, time);
        await incompatible.ReconcileAsync();
        Assert.False(incompatible.IsRunning);
    }

    [Fact]
    public void Connection_string_uses_the_installation_settings_and_component_name()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Erp:Instance"] = "PROD",
            ["Database:Server"] = @"SQL01\ERP",
            ["Database:Name"] = "ERP_PROD",
            ["Database:Authentication"] = "Sql",
            ["Database:UserId"] = "erp_prod_app",
            ["Database:Password"] = "p@ss",
            ["Database:Encrypt"] = "Strict",
            ["Database:TrustServerCertificate"] = "false",
            ["Database:CommandTimeoutSeconds"] = "45",
        }).Build();

        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(InstallationConfiguration.ConnectionString(configuration, "Host"));

        Assert.Equal("ERP-PROD-Host", builder.ApplicationName);
        Assert.Equal(("erp_prod_app", "p@ss", 45), (builder.UserID, builder.Password, builder.CommandTimeout));
        Assert.Equal(Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Strict, builder.Encrypt);

        configuration["Database:Authentication"] = "Windows";
        Assert.True(new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(InstallationConfiguration.ConnectionString(configuration, "Worker")).IntegratedSecurity);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Installation_configuration_reads_settings_and_dpapi_secrets()
    {
        var paths = new Erp.Configuration.InstanceDataPaths(folder);
        Directory.CreateDirectory(paths.ConfigDirectory);
        File.WriteAllText(paths.AppSettings, """{ "Erp": { "Instance": "PROD" }, "Database": { "UserId": "erp_prod_app" } }""");
        Erp.Configuration.InstallationSecrets.Write(paths.Secrets, new Dictionary<string, string> { ["Database:Password"] = "secret" }, "PROD");

        var configuration = new ConfigurationBuilder().AddErpInstallation(folder).Build();

        Assert.Equal("erp_prod_app", configuration["Database:UserId"]);
        Assert.Equal("secret", configuration["Database:Password"]);
    }

    [Fact]
    public void Build_info_is_read_from_assembly_metadata()
    {
        var info = ErpBuildInfo.From(typeof(HostingTests).Assembly);

        Assert.False(string.IsNullOrEmpty(info.Version));
        Assert.Null(info.LastMigration);
    }

    private sealed class CountingFactory : IBackgroundProcessingFactory
    {
        public int Started { get; private set; }

        public int Stopped { get; private set; }

        public IAsyncDisposable Start()
        {
            Started++;
            return new Stopper(this);
        }

        private sealed class Stopper(CountingFactory owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Stopped++;
                return ValueTask.CompletedTask;
            }
        }
    }
}
