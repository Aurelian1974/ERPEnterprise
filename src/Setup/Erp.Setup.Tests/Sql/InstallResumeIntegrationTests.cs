using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Health;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Engine;
using Erp.Setup.Sql;
using Erp.Setup.Tests.Checks;
using Erp.Setup.Windows.Certificates;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>
/// Planul instalarii (08, 5.5) prin orchestrator, pe SQL Server real, cu operatiile Windows simulate: intreruperea si reluarea
/// cu acelasi installId. Loginurile Windows (I-11) nu exista in containerul Linux, deci pasul e inlocuit; pasii Windows reali
/// si IA-03 complet cer E2E pe o masina virtuala Windows.
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

    private readonly FakeWindowsSetup windows = new();

    private InstallRequest Request(string instance, string database) => new(instance, @"C:\Program Files\Erp",
        new SqlRequest(sql.Server, database, null, null, AppIdentity.VirtualAccounts, null),
        new SiteRequest("erp.test.local", 443, true, CertificateKind.Internal, null),
        new BackupRequest(null, new TimeOnly(1, 0), 15, 14, DayOfWeek.Saturday, new TimeOnly(2, 0)),
        CheckFixtures.WriteTemp(licenses.Create(TestLicenses.Payload())), new HashSet<string>())
    {
        DataRoot = dataRoot,
    };

    private InstallSession Session(InstallRequest request)
    {
        var log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(logSink).CreateLogger();
        return InstallSession.ForTests(request, CheckFixtures.Manifest, kit.Root, sql.Operator(log), sqlIsExpress: true);
    }

    private IReadOnlyList<PlannedStep> Plan(InstallSession session, string? failAt)
    {
        var plan = PlanBuilder.Install(session, windows, licenses.Reader, TimeProvider.System);
        Assert.Equal(PlanBuilder.InstallStepIds, plan.Select(p => p.Step.Id));
        return [.. plan.Select(p => p.Step.Id switch
        {
            "I-11" => new PlannedStep(new DelegateStep("I-11", p.Step.Title,
                (_, _) => Task.FromResult(StepResult.Skipped("conturile Windows nu există în containerul Linux"))), p.FailureExitCode),
            var id when id == failAt => new PlannedStep(new DelegateStep(id, "întrerupere simulată",
                (_, _) => throw new IOException("Conexiune pierdută")), p.FailureExitCode),
            _ => p,
        })];
    }

    private async Task<OrchestrationResult> Install(InstallSession session, string? failAt = null)
    {
        var context = new SetupContext { Log = session.Sql.Log, KitRoot = kit.Root, Unattended = true };
        return await new StepOrchestrator(new NullReporter(), session).RunAsync(Plan(session, failAt), context, Ct);
    }

    private async Task<T?> Scalar<T>(string database, string query)
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        return await SqlOperator.ScalarAsync<T>(connection, query, Ct);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-03")]
    public async Task Interrupted_install_resumes_with_the_same_install_id_and_one_administrator()
    {
        var request = Request("ITINSR", "ERP_IT_INSTRESUME");

        var interrupted = await Install(Session(request), failAt: "I-13");

        Assert.Equal(ExitCodes.InvalidLicense, interrupted.ExitCode);
        Assert.Equal("I-13", interrupted.FailedStep);
        var state = InstanceStateStore.Read(InstanceStateStore.PathFor(dataRoot, "ITINSR"))!;
        Assert.Equal(InstanceLifecycle.Installing, state.State);
        Assert.Equal(PlanBuilder.InstallStepIds.Take(12), state.CompletedSteps);
        Assert.Equal(FakeWindowsSetup.HttpsThumbprint, state.CertificateThumbprint);
        Assert.Equal(FakeWindowsSetup.DataProtectionThumbprint, state.DataProtectionCertificateThumbprint);
        Assert.Equal(BackupEdition.None, state.BackupEdition);

        // A doua rulare se opreste la health-check, dupa crearea administratorului.
        windows.Health = new HealthCheckOutcome(false, "database: Unhealthy", null);
        var resumed = Session(request);
        var unhealthy = await Install(resumed);

        Assert.Equal(ExitCodes.HealthCheckFailed, unhealthy.ExitCode);
        Assert.True(resumed.Resuming);
        Assert.Equal(state.InstallId, resumed.InstallId);
        Assert.Equal(state.InstallId.ToString(), await Scalar<string>("ERP_IT_INSTRESUME",
            "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstallId';"), ignoreCase: true);
        Assert.True(resumed.Administrator!.Created);
        Assert.NotNull(resumed.Administrator.TemporaryPassword);
        Assert.DoesNotContain(resumed.Administrator.TemporaryPassword!, logSink.Text);

        // A treia rulare termina instalarea: nimic duplicat, administratorul nu se recreeaza.
        windows.Health = new HealthCheckOutcome(true, "Healthy", null);
        var again = Session(request);
        Assert.Equal(ExitCodes.SuccessWithWarnings, (await Install(again)).ExitCode);
        Assert.False(again.Administrator!.Created);
        Assert.Null(again.InitialAdminFilePath);
        Assert.Equal(1, await Scalar<int>("ERP_IT_INSTRESUME", "SELECT COUNT(*) FROM ids.UserAccount;"));
        Assert.Equal(1, await Scalar<int>("ERP_IT_INSTRESUME", "SELECT COUNT(*) FROM core.InstallationLicense;"));

        var final = InstanceStateStore.Read(again.StatePath)!;
        Assert.Equal(InstanceLifecycle.Installed, final.State);
        Assert.Equal(CheckFixtures.Manifest.Version, final.CurrentVersion);
        Assert.Equal(PlanBuilder.InstallStepIds, final.CompletedSteps);
        var answers = AnswersValidator.Load(again.Paths.Answers);
        Assert.True(answers.IsValid);
        Assert.Equal(request.Sql, answers.Request!.Sql);
        Assert.Equal(2, windows.Calls.Count(c => c == "WriteConfiguration"));
        Assert.Equal(1, windows.Calls.Count(c => c == "CreateInternalHttpsCertificate"));
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

/// <summary>Operatiile Windows simulate: inregistreaza apelurile, creeaza doar folderele instantei.</summary>
internal sealed class FakeWindowsSetup : IWindowsSetup
{
    public const string HttpsThumbprint = "3F2A9C00112233445566778899AABBCCDDEEFFE1";
    public const string DataProtectionThumbprint = "9C41D000112233445566778899AABBCCDDEEFF7A";

    public List<string> Calls { get; } = [];

    public HealthCheckOutcome Health { get; set; } = new(true, "Healthy", null);

    private readonly HashSet<string> certificates = new(StringComparer.OrdinalIgnoreCase);

    public void CreateDataFolders(InstallRequest request)
    {
        Calls.Add(nameof(CreateDataFolders));
        Directory.CreateDirectory(Erp.Configuration.InstanceDataPaths.For(request.DataRoot, request.Instance).InstallDirectory);
    }

    public IReadOnlyList<string> MissingIisFeatures() => [];

    public bool EnableIisFeatures(IReadOnlyList<string> missing) => false;

    public bool IsHostingBundleInstalled(string minimumVersion) => true;

    public bool InstallHostingBundle(string kitRoot) => false;

    public string CreateInternalHttpsCertificate(InstallRequest request)
    {
        Calls.Add(nameof(CreateInternalHttpsCertificate));
        certificates.Add(HttpsThumbprint);
        return HttpsThumbprint;
    }

    public CertificateInfo? FindMachineCertificate(string thumbprint) =>
        certificates.Contains(thumbprint) ? new CertificateInfo(true, DateTimeOffset.UtcNow.AddYears(2), ["erp.test.local"], true) : null;

    public int DeployBinaries(string kitRoot, KitManifest manifest, InstallRequest request) => 0;

    public void CreateSiteAndService(InstallRequest request, string version, string certificateThumbprint) => Calls.Add(nameof(CreateSiteAndService));

    public void WriteConfiguration(string kitRoot, InstallRequest request, string version, InstallationSettingsValues values) =>
        Calls.Add(nameof(WriteConfiguration));

    public string EnsureDataProtectionCertificate(InstallRequest request, string? existingThumbprint) => DataProtectionThumbprint;

    public string ComputerAccount() => @"TEST\SRV-ERP$";

    public void ApplyFirewall(InstallRequest request) => Calls.Add(nameof(ApplyFirewall));

    public void StartApplication(InstallRequest request, string version) => Calls.Add(nameof(StartApplication));

    public Task<HealthCheckOutcome> CheckHealthAsync(InstallRequest request, string version, string? lastMigration, string? pinnedThumbprint,
        CancellationToken ct) => Task.FromResult(Health);

    public IReadOnlyList<string> ComponentLogTail(InstallRequest request) => ["(jurnal simulat)"];

    public string WriteInitialAdminFile(InstallRequest request, string content) => throw new InvalidOperationException("Nu se apelează în acest test.");

    public void ExportRecoveryKey(string thumbprint, string pfxPath, string password) => Calls.Add(nameof(ExportRecoveryKey));

    public RecoveryKeyCheck VerifyRecoveryKey(string pfxPath, string password, string? expectedThumbprint) => RecoveryKeyCheck.Valid;
}
