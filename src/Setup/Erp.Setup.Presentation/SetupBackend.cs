using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.Logging;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Engine;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Probes;
using Erp.Setup.Windows.Certificates;
using Erp.Setup.Windows.Probes;
using Serilog.Core;

namespace Erp.Setup.Presentation;

/// <summary>
/// Backend-ul real al ferestrei: aceleasi verificari, acelasi plan si aceeasi cheie de recuperare ca ErpSetup.Cli.exe. Jurnalul
/// e in %TEMP%\ErpSetup pana la crearea instantei, apoi se muta in C:\ProgramData\Erp\&lt;Instanta&gt;\logs\setup (5.2).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SetupBackend : ISetupBackend, IDisposable
{
    private const string ServerAuthenticationEku = "1.3.6.1.5.5.7.3.1";

    private readonly string kitRoot;
    private readonly TimeProvider time;
    private readonly SetupLogFiles logFiles;
    private readonly Logger log;
    private readonly WindowsProbe probe = new();
    private string? moveLogsTo;

    public SetupBackend(string kitRoot, KitManifest manifest, TimeProvider time)
    {
        this.kitRoot = kitRoot;
        this.time = time;
        Manifest = manifest;
        logFiles = SetupLog.CreateFiles(SetupLog.DefaultTempDirectory, time.GetUtcNow());
        log = SetupLog.Create(logFiles, Serilog.Events.LogEventLevel.Information);
        log.Information("ErpSetup {Version}, kitul {KitVersion}", typeof(SetupBackend).Assembly.GetName().Version, manifest.Version);
    }

    public KitManifest Manifest { get; }

    public string CurrentUser
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
    }

    public string DefaultHostName
    {
        get
        {
            var host = Dns.GetHostEntry(string.Empty).HostName;
            return string.IsNullOrEmpty(host) ? Environment.MachineName.ToLowerInvariant() : host.ToLowerInvariant();
        }
    }

    public string DataRoot => InstallRequest.DefaultDataRoot;

    public string LogPath => logFiles.TextPath;

    public IReadOnlyList<InstanceState> FindInstallations() => InstanceStateStore.FindAll(DataRoot);

    public async Task<SqlConnectionTest> TestSqlConnectionAsync(string server, string database, CancellationToken ct)
    {
        try
        {
            await using var sql = await new SqlServerConnector().ConnectAsync(new SqlTarget(server, database, "SETUP"), ct);
            var info = await sql.GetServerInfoAsync(ct);
            var agent = info.IsExpress ? "lipsește (Express)" : await sql.GetAgentStatusAsync(ct);
            var existing = await sql.GetDatabaseAsync(database, ct);
            var remote = !string.Equals(info.MachineName, probe.MachineName, StringComparison.OrdinalIgnoreCase);
            return new SqlConnectionTest(true, "Conectat.", info, existing, sql.TrustedServerCertificate, remote, agent);
        }
        catch (SqlConnectFailedException ex)
        {
            log.Warning(ex, "Testul conexiunii SQL a eșuat");
            return new SqlConnectionTest(false, ex.Message);
        }
    }

    public IReadOnlyList<CertificateChoice> FindCertificates(string hostName)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var now = time.GetUtcNow();
        var result = new List<CertificateChoice>();
        foreach (var certificate in store.Certificates)
        {
            using (certificate)
            {
                var names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().SelectMany(e => e.EnumerateDnsNames());
                var serverAuth = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                    .Any(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Any(o => o.Value == ServerAuthenticationEku));
                if (certificate.HasPrivateKey && serverAuth && certificate.NotAfter.ToUniversalTime() > now.UtcDateTime
                    && names.Any(n => RequirementsChecker.DnsNameMatches(n, hostName)))
                {
                    result.Add(new CertificateChoice(certificate.Thumbprint, certificate.GetNameInfo(X509NameType.SimpleName, false),
                        new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero)));
                }
            }
        }

        return [.. result.OrderByDescending(c => c.NotAfter)];
    }

    public LicenseReadResult ReadLicense(string path) =>
        new LicenseReader(LicenseKeys.Embedded, time).Read(File.ReadAllBytes(path));

    public async Task<RequirementsReport> CheckAsync(InstallRequest request, CancellationToken ct)
    {
        using var network = new HttpNetworkProbe();
        var checker = new RequirementsChecker(probe, new SqlServerConnector(), network, new LicenseReader(LicenseKeys.Embedded, time), time);
        var existing = InstanceStateStore.Read(InstanceStateStore.PathFor(request.DataRoot, request.Instance));
        var report = await checker.RunAsync(CheckTarget.From(request, CheckMode.Install), Manifest,
            new KitCheck(true, $"{Manifest.Product} {Manifest.Version}"), existing, ct);
        foreach (var result in report.Results)
        {
            log.Information("[{Id}] {Status} {Title}: {Message}", result.Id, result.Status, result.Title, result.Message);
        }

        return report;
    }

    public async Task<InstallOutcome> InstallAsync(InstallRequest request, RequirementsReport report, IStepReporter reporter, CancellationToken ct)
    {
        var sql = new SqlOperator(new SqlTarget(request.Sql.Server, request.Sql.Database, request.Instance, report.TrustServerCertificate), log);
        var session = InstallSession.Start(request, Manifest, kitRoot, sql, report, unattended: false);
        var windows = new WindowsSetup(log, time);
        var plan = PlanBuilder.Install(session, windows, new LicenseReader(LicenseKeys.Embedded, time), time);
        var context = new SetupContext { Log = log, KitRoot = kitRoot, Unattended = false };

        // Orchestratorul ruleaza in fundal; reporter-ul trece pe firul ferestrei prin IProgress.
        var result = await Task.Run(() => new StepOrchestrator(reporter, session).RunAsync(plan, context, ct), ct);
        if (session.State is not null)
        {
            moveLogsTo = session.Paths.SetupLogs;
        }

        var failed = result.Steps.LastOrDefault(s => s.Result.Outcome == StepOutcome.Failed);
        var state = session.State;
        return new InstallOutcome(
            result.ExitCode,
            result.FailedStep,
            failed.Result?.Message,
            AppSettingsGenerator.PublicOrigin(request.Site.HostName, request.Site.HttpsPort) + "/",
            session.Administrator is { Created: true } ? session.Administrator.UserName : null,
            session.Administrator is { Created: true } ? session.Administrator.TemporaryPassword : null,
            state?.CertificateKind == CertificateKind.Internal ? session.Paths.InternalCertificate : null,
            session.RestartRequired);
    }

    public void ExportRecoveryKey(InstallRequest request, string pfxPath, string password) =>
        RecoveryKeys.Export(new WindowsSetup(log, time), InstanceStateStore.PathFor(request.DataRoot, request.Instance), pfxPath, password, time);

    public void WriteAnswers(string path, InstallRequest request) => AnswersFile.Write(path, request);

    public void Dispose()
    {
        log.Dispose();
        if (moveLogsTo is not null && Directory.Exists(moveLogsTo))
        {
            try
            {
                SetupLog.MoveFiles(logFiles, moveLogsTo);
            }
            catch (IOException)
            {
                // Jurnalul ramane in %TEMP%\ErpSetup.
            }
        }
    }
}
