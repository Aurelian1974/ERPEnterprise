using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Install;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Logins;
using Erp.Setup.Tests.Checks;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>
/// Pasii SQL ai planului install (I-05…I-14) prin orchestrator, pe SQL Server real: intreruperea dupa I-10 si reluarea (8.1).
/// Pasii Windows (IIS, servicii, NTFS) si IA-01/IA-06/IA-08 complet cer E2E pe VM dedicata.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class InstallResumeIntegrationTests(SqlServerFixture sql) : IDisposable
{
    private readonly TestLicenses licenses = new();
    private readonly TempKitDatabase kit = KitWithBootstrap();
    private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "erp-install-" + Guid.NewGuid().ToString("N"));
    private readonly CapturingSink logSink = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        licenses.Dispose();
        kit.Dispose();
        if (Directory.Exists(dataRoot))
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static TempKitDatabase KitWithBootstrap()
    {
        var kit = TempKitDatabase.FromRepository();
        var bootstrap = Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap");
        foreach (var file in Directory.EnumerateFiles(bootstrap, "*.sql"))
        {
            kit.Add($"bootstrap/{Path.GetFileName(file)}", File.ReadAllText(file));
        }

        return kit;
    }

    private InstallAnswers Answers(string instance, string database) => new(instance, @"C:\Program Files\Erp", dataRoot,
        new SiteAnswers("erp.test.local", 443, true, null, true),
        new SqlAnswers(sql.Server, database, SqlAuthentication.Sql, "sa", SqlAuthentication.Sql, null, null, null, 256, 256, SqlEncrypt.Optional, true),
        new BackupAnswers(@"B:\SQLBackup", new TimeOnly(1, 0), 15, 14, DayOfWeek.Sunday, new TimeOnly(3, 0)),
        CheckFixtures.WriteTemp(licenses.Create(TestLicenses.Payload())),
        new AdministratorAnswers("administrator", "Administrator ERP", null),
        new PrerequisitesAnswers(true, true));

    /// <summary>Echivalentul I-02: raspunsurile, conexiunea operatorului si, daca exista, instalarea intrerupta din instance.json.</summary>
    private InstallSession Session(InstallAnswers answers)
    {
        var log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(logSink).CreateLogger();
        var session = new InstallSession
        {
            KitRoot = kit.Root,
            DatabaseRoot = kit.Root,
            Manifest = CheckFixtures.Manifest,
            Unattended = true,
            AssumeYes = true,
            Answers = answers,
            Sql = new SqlOperator(sql.Target("master"), sql.SaPassword, log),
        };
        if (InstanceStateStore.Read(session.StatePath) is { State: InstanceLifecycle.Installing } existing)
        {
            session.State = existing;
            session.InstallId = existing.InstallId;
            session.Resuming = true;
        }

        return session;
    }

    private IReadOnlyList<PlannedStep> SqlPlan(InstallSession session, bool failAfterProgrammability = false)
    {
        var verifier = new LicenseVerifier(licenses.PublicKeys, TimeProvider.System);
        var steps = new List<PlannedStep>
        {
            SqlInstallSteps.I05_DataFolders(session),
            SqlInstallSteps.I06_Database(session),
            SqlInstallSteps.I07_Bootstrap(session),
            SqlInstallSteps.I08_Logins(session),
            SqlInstallSteps.I09_Migrations(session),
            SqlInstallSteps.I10_Programmability(session),
        };
        if (failAfterProgrammability)
        {
            steps.Add(new PlannedStep(new DelegateStep("I-11", "întrerupere simulată", (_, _) => throw new IOException("Conexiune pierdută")),
                ExitCodes.SqlInstallError));
        }

        steps.AddRange([
            SqlInstallSteps.I11_Seed(session),
            SqlInstallSteps.I12_License(session, verifier),
            SqlInstallSteps.I13_Administrator(session),
            SqlInstallSteps.I14_DisableMigrator(session),
        ]);
        return steps;
    }

    /// <summary>Ca InstallCommand: orchestratorul, apoi dezactivarea migratorului in finally.</summary>
    private async Task<OrchestrationResult> Install(InstallSession session, bool failAfterProgrammability = false)
    {
        var context = new SetupContext { Log = session.Sql!.Log, KitRoot = kit.Root, Unattended = true };
        try
        {
            return await new StepOrchestrator(new NullReporter(), session).RunAsync(SqlPlan(session, failAfterProgrammability), context, Ct);
        }
        finally
        {
            await SqlInstallSteps.DisableMigratorIfEnabledAsync(session, Ct);
        }
    }

    private async Task<T?> Scalar<T>(string database, string query)
    {
        var builder = Erp.Setup.Sql.Probes.SqlServerConnector.BuildOperatorConnectionString(sql.Target(database), sql.SaPassword);
        builder.InitialCatalog = database;
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(Ct);
        return await SqlOperator.ScalarAsync<T>(connection, query, Ct);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-08")]
    public async Task Interrupted_install_resumes_with_the_same_install_id_and_one_administrator()
    {
        var answers = Answers("ITRES", "ERP_IT_RESUME");
        var migrator = SqlPrincipals.For("ITRES", SqlAuthentication.Sql, null).MigratorLogin;

        var interrupted = await Install(Session(answers), failAfterProgrammability: true);

        Assert.Equal(ExitCodes.SqlInstallError, interrupted.ExitCode);
        Assert.Equal("I-11", interrupted.FailedStep);
        var state = InstanceStateStore.Read(InstanceStateStore.PathFor(dataRoot, "ITRES"))!;
        Assert.Equal(InstanceLifecycle.Installing, state.State);
        Assert.Equal(["I-05", "I-06", "I-07", "I-08", "I-09", "I-10"], state.CompletedSteps);
        Assert.Equal(1, await Scalar<int>("master", $"SELECT CAST(is_disabled AS int) FROM sys.server_principals WHERE name = N'{migrator}';"));

        var resumed = Session(answers);
        var completed = await Install(resumed);

        Assert.Equal(ExitCodes.Success, completed.ExitCode);
        Assert.True(resumed.Resuming);
        Assert.Equal(state.InstallId, resumed.InstallId);
        Assert.Equal(state.InstallId.ToString(), await Scalar<string>("ERP_IT_RESUME",
            "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstallId';"), ignoreCase: true);
        Assert.True(resumed.Administrator!.Created);
        Assert.NotNull(resumed.Administrator.TemporaryPassword);
        Assert.DoesNotContain(resumed.Administrator.TemporaryPassword!, logSink.Text);
        Assert.Contains("I-14", InstanceStateStore.Read(resumed.StatePath)!.CompletedSteps);
        Assert.Equal(1, await Scalar<int>("master", $"SELECT CAST(is_disabled AS int) FROM sys.server_principals WHERE name = N'{migrator}';"));

        // A doua reluare (de exemplu dupa un esec la IIS): nimic duplicat, administratorul nu se recreeaza.
        var again = Session(answers);
        Assert.Equal(ExitCodes.Success, (await Install(again)).ExitCode);
        Assert.False(again.Administrator!.Created);
        Assert.Equal(1, await Scalar<int>("ERP_IT_RESUME", "SELECT COUNT(*) FROM ids.UserAccount;"));
        Assert.Equal(2, await Scalar<int>("ERP_IT_RESUME", "SELECT COUNT(*) FROM core.[User];"));
        Assert.Equal(4, await Scalar<int>("ERP_IT_RESUME", "SELECT COUNT(*) FROM core.Role WHERE IsSystem = 1;"));
        Assert.Equal(1, await Scalar<int>("ERP_IT_RESUME", "SELECT COUNT(*) FROM core.InstallationLicense;"));
        Assert.Equal(1, await Scalar<int>("ERP_IT_RESUME", "SELECT COUNT(*) FROM core.AuditEntry;"));
        Assert.True(File.Exists(Path.Combine(dataRoot, "ITRES", "license", "erp.lic")));
    }

    private sealed class NullReporter : IStepReporter
    {
        public void Started(IInstallStep step)
        {
        }

        public void Finished(IInstallStep step, StepResult result, TimeSpan elapsed)
        {
        }
    }
}
