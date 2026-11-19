using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Health;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Engine;
using Erp.Setup.Sql;
using Erp.Setup.Tests.Checks;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>
/// Actualizarea cu revenire automata (08, 10.2), „Repară” (11.1) si „Resetează accesul administratorului” (11.3) pe SQL Server real,
/// cu operatiile Windows simulate. Partea Windows reala (IIS, serviciu, app_offline.htm) a IA-10 ramane E2E pe masini virtuale.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class UpgradeIntegrationTests(SqlServerFixture sql) : IDisposable
{
    private const string BackupFolder = "/var/opt/mssql/data/erp-upgrade-it";
    private const string GoodMigration = "migrations/core/2099.01.001_Add_UpgradeProbe.sql";
    private const string DefectiveMigration = "migrations/core/2099.01.002_Defect.sql";

    private readonly TestLicenses licenses = new();
    private readonly List<TempKitDatabase> kits = [];
    private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "erp-upgrade-" + Guid.NewGuid().ToString("N"));
    private readonly FakeWindowsSetup windows = new();
    private readonly CapturingSink logSink = new();
    private readonly RecordingReporter reporter = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ILogger Log => new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(logSink).CreateLogger();

    public void Dispose()
    {
        licenses.Dispose();
        kits.ForEach(k => k.Dispose());
        if (Directory.Exists(dataRoot))
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private TempKitDatabase Kit(params string[] extraMigrations)
    {
        var kit = TempKitDatabase.FromRepository();
        foreach (var file in Directory.EnumerateFiles(TestPaths.BootstrapDirectory(), "*.sql"))
        {
            kit.Add($"bootstrap/{Path.GetFileName(file)}", File.ReadAllText(file));
        }

        foreach (var migration in extraMigrations)
        {
            kit.Add(migration, migration == DefectiveMigration
                ? "THROW 50000, N'Migrare defectă intenționat (IA-10).', 1;"
                : "CREATE TABLE core.UpgradeProbe (Id int NOT NULL CONSTRAINT PK_UpgradeProbe PRIMARY KEY);");
        }

        kits.Add(kit);
        return kit;
    }

    private static KitManifest Manifest(string version) => CheckFixtures.Manifest with { Version = version };

    private InstallRequest Request(string instance, string database) => new(instance, @"C:\Program Files\Erp",
        new SqlRequest(sql.Server, database, null, null, AppIdentity.VirtualAccounts, null),
        new SiteRequest("erp.test.local", 443, true, CertificateKind.Internal, null),
        new BackupRequest(BackupFolder, new TimeOnly(1, 0), 15, 14, DayOfWeek.Saturday, new TimeOnly(2, 0)),
        CheckFixtures.WriteTemp(licenses.Create(TestLicenses.Payload())), new HashSet<string>())
    {
        DataRoot = dataRoot,
    };

    /// <summary>Instalarea 2027.1.0 prin plan (loginurile Windows, I-11, nu exista in container).</summary>
    private async Task<InstallRequest> InstallAsync(string instance, string database)
    {
        var request = Request(instance, database);
        var session = InstallSession.ForTests(request, Manifest("2027.1.0"), Kit().Root, sql.Operator(Log), sqlIsExpress: true, backupFolder: BackupFolder);
        var result = await Run(PlanBuilder.Install(session, windows, licenses.Reader, TimeProvider.System), session);
        Assert.True(result.Succeeded, string.Join("; ", result.Steps.Select(s => $"{s.StepId} {s.Result.Outcome} {s.Result.Message}")));
        return request;
    }

    private async Task<OrchestrationResult> Run(IReadOnlyList<PlannedStep> plan, IStepJournal journal)
    {
        var steps = plan.Select(p => p.Step.Id == "I-11"
            ? new PlannedStep(new DelegateStep("I-11", p.Step.Title, (_, _) => Task.FromResult(StepResult.Skipped("container Linux"))), p.FailureExitCode)
            : p).ToList();
        return await new StepOrchestrator(reporter, journal).RunAsync(steps, new SetupContext { Log = Log, KitRoot = dataRoot, Unattended = true }, Ct);
    }

    private async Task<UpgradeOutcome> UpgradeAsync(InstallRequest request, bool useSnapshot, params string[] migrations)
    {
        var state = InstanceStateStore.Read(InstanceStateStore.PathFor(dataRoot, request.Instance))!;
        var session = UpgradeSession.ForTests(request, state, Manifest("2027.2.0"), Kit(migrations).Root, sql.Operator(Log), BackupFolder, useSnapshot);
        return await new UpgradeRunner(session, windows, licenses.Reader, TimeProvider.System, reporter)
            .RunAsync(new SetupContext { Log = Log, KitRoot = dataRoot, Unattended = true }, Ct);
    }

    private async Task<T?> Scalar<T>(string database, string query)
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        return await SqlOperator.ScalarAsync<T>(connection, query, Ct);
    }

    private Task<string?> LastMigration(string database) => Scalar<string>(database, "SELECT TOP (1) ScriptName FROM core.SchemaVersion ORDER BY Id DESC;");

    [Theory]
    [Trait("Criteriu", "08-IA-10")]
    [InlineData(true, "din snapshot")]
    [InlineData(false, "din backup")]
    public async Task Defective_migration_rolls_back_automatically_with_the_database_as_before(bool useSnapshot, string source)
    {
        var database = useSnapshot ? "ERP_IT_UPGSNAP" : "ERP_IT_UPGBAK";
        var request = await InstallAsync(useSnapshot ? "ITUPS" : "ITUPB", database);
        var before = await LastMigration(database);
        await sql.ExecuteAsync("INSERT INTO core.AuditEntry (OccurredAt, UserId, Action, EntityType, Changes, CorrelationId) " +
            "VALUES (SYSUTCDATETIME(), 1, N'Test.Before', N'Test', N'{}', NEWID());", database);

        var outcome = await UpgradeAsync(request, useSnapshot, GoodMigration, DefectiveMigration);

        Assert.Equal(ExitCodes.UpgradeFailedRolledBack, outcome.ExitCode);
        Assert.Equal("U-06", outcome.FailedStep);
        Assert.Contains(reporter.Finished, f => f.Id == "R-02" && f.Result.Message!.Contains(source, StringComparison.Ordinal));
        Assert.Equal(before, await LastMigration(database));
        Assert.Null(await Scalar<int?>(database, "SELECT OBJECT_ID(N'core.UpgradeProbe');"));
        Assert.Equal(1, await Scalar<int>(database, "SELECT COUNT(*) FROM core.AuditEntry WHERE Action = N'Test.Before';"));
        Assert.Equal("MULTI_USER", await Scalar<string>("master", $"SELECT user_access_desc FROM sys.databases WHERE name = N'{database}';"));
        Assert.Equal(0, await Scalar<int>("master", $"SELECT COUNT(*) FROM sys.databases WHERE name = N'{database}_UPG';"));

        var state = InstanceStateStore.Read(InstanceStateStore.PathFor(dataRoot, request.Instance))!;
        Assert.Equal(InstanceLifecycle.Installed, state.State);
        Assert.Equal("2027.1.0", state.CurrentVersion);
        Assert.False(File.Exists(UpgradeState.PathFor(dataRoot, request.Instance)));
        Assert.Contains("SwitchVersion 2027.1.0", windows.Calls);
        Assert.Contains("RemoveAppOffline 2027.1.0", windows.Calls);
        Assert.Contains("ClearMaintenanceFlag", windows.Calls);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-10")]
    public async Task Successful_upgrade_keeps_the_data_and_moves_to_the_new_version()
    {
        var request = await InstallAsync("ITUPOK", "ERP_IT_UPGOK");

        var outcome = await UpgradeAsync(request, useSnapshot: true, GoodMigration);

        Assert.Equal(ExitCodes.Success, outcome.ExitCode);
        Assert.Equal("core/2099.01.001_Add_UpgradeProbe.sql", await LastMigration("ERP_IT_UPGOK"));
        Assert.Equal(1, await Scalar<int>("ERP_IT_UPGOK", "SELECT COUNT(*) FROM ids.UserAccount;"));
        Assert.Equal(0, await Scalar<int>("master", "SELECT COUNT(*) FROM sys.databases WHERE name = N'ERP_IT_UPGOK_UPG';"));
        var state = InstanceStateStore.Read(InstanceStateStore.PathFor(dataRoot, request.Instance))!;
        Assert.Equal(("2027.2.0", InstanceLifecycle.Installed), (state.CurrentVersion, state.State));
        Assert.Equal(["2027.1.0"], state.PreviousVersions);
        Assert.Single(state.UpgradeBackups);
        Assert.Contains("SwitchVersion 2027.2.0", windows.Calls);
        Assert.Contains("Health 2027.2.0", windows.Calls);
    }

    [Fact]
    public async Task Same_or_older_kit_is_refused_with_code_72_without_changes()
    {
        var request = await InstallAsync("ITUPSAME", "ERP_IT_UPGSAME");
        var state = InstanceStateStore.Read(InstanceStateStore.PathFor(dataRoot, request.Instance))!;
        var session = UpgradeSession.ForTests(request, state, Manifest("2027.1.0"), Kit().Root, sql.Operator(Log), BackupFolder, useSnapshot: true);

        var outcome = await new UpgradeRunner(session, windows, licenses.Reader, TimeProvider.System, reporter)
            .RunAsync(new SetupContext { Log = Log, KitRoot = dataRoot, Unattended = true }, Ct);

        Assert.Equal(ExitCodes.UnsupportedUpgradeVersion, outcome.ExitCode);
        Assert.DoesNotContain(windows.Calls, c => c.StartsWith("EnterMaintenance", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-14")]
    public async Task Repair_keeps_users_and_the_data_protection_certificate_and_reset_admin_issues_a_new_temporary_password()
    {
        var request = await InstallAsync("ITREP", "ERP_IT_REPAIR");
        var state = InstanceStateStore.Read(InstanceStateStore.PathFor(dataRoot, request.Instance))!;
        var repair = InstallSession.ForTests(request, Manifest("2027.1.0"), Kit().Root, sql.Operator(Log), sqlIsExpress: true, backupFolder: BackupFolder,
            mode: SetupMode.Repair);

        var result = await Run(PlanBuilder.Install(repair, windows, licenses.Reader, TimeProvider.System), repair);

        Assert.True(result.Succeeded);
        Assert.Equal(StepOutcome.Skipped, result.Steps.Single(s => s.StepId == "I-14").Result.Outcome);
        Assert.Equal(1, windows.Calls.Count(c => c == "CreateInternalHttpsCertificate"));
        Assert.Equal(state.InstallId, InstanceStateStore.Read(repair.StatePath)!.InstallId);
        Assert.Equal(1, await Scalar<int>("ERP_IT_REPAIR", "SELECT COUNT(*) FROM ids.UserAccount;"));

        var password = await AdministratorAccess.ResetAsync(state, sql.Operator(Log), "2027.1.0", Ct);

        Assert.Equal(20, password.Length);
        Assert.DoesNotContain(password, logSink.Text);
        Assert.Equal(1, await Scalar<int>("ERP_IT_REPAIR",
            "SELECT COUNT(*) FROM ids.UserAccount WHERE UserName = N'administrator' AND MustChangePassword = 1 AND TwoFactorRequired = 1 AND TwoFactorEnabled = 0;"));
        Assert.Equal(1, await Scalar<int>("ERP_IT_REPAIR", "SELECT COUNT(*) FROM core.AuditEntry WHERE Action = N'Core.User.EmergencyReset';"));
    }

    private sealed class RecordingReporter : IStepReporter
    {
        public List<(string Id, StepResult Result)> Finished { get; } = [];

        public void Started(IInstallStep step)
        {
        }

        void IStepReporter.Finished(IInstallStep step, StepResult result, TimeSpan elapsed) => Finished.Add((step.Id, result));
    }
}
