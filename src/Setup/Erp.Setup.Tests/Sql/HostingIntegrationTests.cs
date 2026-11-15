using Erp.Hosting;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Logins;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>Verificarile din /health/ready si protectia de versiune pe SQL Server real (8.14, 10.5, IA-18).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class HostingIntegrationTests(SqlServerFixture sql)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SqlOperator Operator() => new(sql.Target("master"), sql.SaPassword, new LoggerConfiguration().CreateLogger());

    private async Task<(string ConnectionString, string LastMigration)> Deploy(string database, string instance)
    {
        var op = Operator();
        await new DatabaseCreator(op).CreateAsync(new DatabaseCreateOptions(database, "IT", Guid.NewGuid(), null, null, 256, 256), Ct);
        await new BootstrapRunner(op).RunAsync(database, Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap"), Ct);
        using var kit = TempKitDatabase.FromRepository();
        var scripts = KitScripts.Load(kit.Root);
        var runner = new MigrationRunner(op.Log);
        await new LoginManager(op, SqlPrincipals.For(instance)).RunAsMigratorAsync(database, (builder, _) =>
        {
            runner.RunMigrations(builder.ConnectionString, scripts);
            runner.RunProgrammability(builder.ConnectionString, scripts);
            runner.RunSeed(builder.ConnectionString, scripts);
            return Task.CompletedTask;
        }, Ct);

        // Aplicatia se conecteaza cu loginul ei, fara db_owner (SP-19), ca in productie.
        var password = SqlPasswordGenerator.Generate();
        var principals = SqlPrincipals.For(instance);
        await new LoginManager(op, principals).EnsureAppLoginAsync(database, Core.Answers.SqlAuthentication.Sql, password, Ct);
        return (op.ConnectionStringFor(database, principals.AppLogin, password).ConnectionString, scripts.LastMigration!);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-18")]
    public async Task Schema_guard_compares_the_last_migration_and_refuses_a_mismatch()
    {
        var (connectionString, lastMigration) = await Deploy("ERP_IT_GUARD", "ITGRD");
        var reader = new SchemaVersionReader(() => new SqlConnection(connectionString));

        var matching = new SchemaVersionGuard(reader, new ErpBuildInfo("2027.1.0", lastMigration), NullLogger<SchemaVersionGuard>.Instance, TimeProvider.System);
        var newerBinaries = new SchemaVersionGuard(reader, new ErpBuildInfo("2027.2.0", "core/2099.01.001_Future.sql"), NullLogger<SchemaVersionGuard>.Instance, TimeProvider.System);

        Assert.Equal(lastMigration, await reader.ReadAsync(Ct));
        Assert.Equal(SchemaCompatibility.Compatible, await matching.CheckAsync(Ct));
        Assert.Equal(SchemaCompatibility.Incompatible, await newerBinaries.CheckAsync(Ct));
        Assert.False(newerBinaries.IsCompatible);

        var schemaCheck = new SchemaCheck(reader, new ErpBuildInfo("2027.2.0", "core/2099.01.001_Future.sql"));
        Assert.Equal(HealthStatus.Unhealthy, await schemaCheck.CheckAsync(Ct));
        Assert.Equal(lastMigration, schemaCheck.LastActual);
        Assert.Equal(HealthStatus.Healthy, await new SchemaCheck(reader, new ErpBuildInfo("2027.1.0", lastMigration)).CheckAsync(Ct));
    }

    [Fact]
    public async Task Database_and_worker_checks_use_the_application_login()
    {
        var (connectionString, _) = await Deploy("ERP_IT_CHECKS", "ITCHK");
        SqlConnection Connect() => new(connectionString);

        Assert.Equal(HealthStatus.Healthy, await new DatabaseCheck(Connect).CheckAsync(Ct));
        Assert.Equal(HealthStatus.Unhealthy, await new WorkerCheck(Connect).CheckAsync(Ct));
        Assert.Equal(HealthStatus.Unhealthy, await new DatabaseCheck(() => new SqlConnection(connectionString.Replace("Password=", "Password=x", StringComparison.Ordinal))).CheckAsync(Ct));

        await sql.ExecuteAsync("""
            INSERT INTO hangfire.[Server] (Id, Data, LastHeartbeat) VALUES (N'old-server', N'{}', DATEADD(MINUTE, -5, SYSUTCDATETIME()));
            """, "ERP_IT_CHECKS");
        Assert.Equal(HealthStatus.Unhealthy, await new WorkerCheck(Connect).CheckAsync(Ct));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-19")]
    public async Task Hangfire_server_runs_on_the_case_sensitive_database_and_makes_the_worker_check_healthy()
    {
        var (connectionString, _) = await Deploy("ERP_IT_HANGFIRE", "ITHGF");
        var storage = new SqlServerStorage(() => new SqlConnection(connectionString), new SqlServerStorageOptions
        {
            SchemaName = "hangfire",
            PrepareSchemaIfNecessary = false,
        });

        using (var server = new BackgroundJobServer(new BackgroundJobServerOptions { ServerName = "ErpWorker-ITHGF:test" }, storage))
        {
            var check = new WorkerCheck(() => new SqlConnection(connectionString));
            var healthy = false;
            for (var i = 0; i < 30 && !healthy; i++)
            {
                healthy = await check.CheckAsync(Ct) == HealthStatus.Healthy;
                if (!healthy)
                {
                    await Task.Delay(500, Ct);
                }
            }

            Assert.True(healthy, "Serverul Hangfire nu a scris heartbeat în hangfire.Server.");

            var jobId = new BackgroundJobClient(storage).Enqueue(() => Console.WriteLine("ERP"));
            string? state = null;
            for (var i = 0; i < 40 && state != "Succeeded"; i++)
            {
                await Task.Delay(500, Ct);
                using var connection = storage.GetConnection();
                state = connection.GetStateData(jobId)?.Name;
            }

            Assert.Equal("Succeeded", state);
            server.SendStop();
        }
    }
}
