using System.Runtime.Versioning;
using Erp.Licensing;
using Erp.Setup.Cli;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Deployment;
using Erp.Setup.Core.Health;
using Erp.Setup.Core.Logging;
using Erp.Setup.Core.Secrets;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Probes;
using Erp.Setup.Windows.Iis;
using Erp.Setup.Windows.Layout;
using Erp.Setup.Windows.Prerequisites;
using Erp.Setup.Windows.Probes;
using Erp.Setup.Windows.Processes;
using Erp.Setup.Windows.Security;
using Erp.Setup.Windows.Services;

namespace Erp.Setup.Install;

/// <summary>Confirmarea operatorului (6.1): fara --unattended, inainte de primul pas care modifica ceva.</summary>
public interface IOperatorPrompt
{
    bool Confirm(string question);
}

/// <summary>Planul complet al instalarii (8.1): I-01…I-24.</summary>
[SupportedOSPlatform("windows")]
internal sealed class InstallPlan(InstallSession session, string answersPath, OperatorConsole console, IOperatorPrompt prompt, TimeProvider time)
{
    private readonly WindowsProbe windows = new();
    private readonly ILicenseReader licenseVerifier = new LicenseReader(LicenseKeys.Embedded, time);

    private InstanceLayout Layout => new(Answers.InstallRoot, Answers.DataRoot, Answers.Instance);

    private InstallAnswers Answers => session.RequireAnswers();

    public IReadOnlyList<PlannedStep> Build(Serilog.ILogger log)
    {
        var processes = new ProcessRunner(log);
        return
        [
            Step("I-01", "Verific semnătura și integritatea kitului", ExitCodes.KitFilesMissingOrModified,
                (_, _) => Task.FromResult(StepResult.Done($"{session.Manifest.Product} {session.Manifest.Version}"))),
            Step("I-02", "Citesc fișierul de răspunsuri și parolele", ExitCodes.InvalidAnswersFile, (context, _) => Task.FromResult(ReadAnswers(context))),
            Step("I-03", "Verific cerințele (C-01…C-23)", ExitCodes.RequirementsNotMet, CheckRequirementsAsync),
            Step("I-04", "Instalez rolurile IIS și Hosting Bundle, dacă lipsesc", ExitCodes.RequirementsNotMet,
                (_, _) => Task.FromResult(InstallPrerequisites(processes))),
            SqlInstallSteps.I05_DataFolders(session, () => Layout.CreateDataFolders()),
            SqlInstallSteps.I06_Database(session),
            SqlInstallSteps.I07_Bootstrap(session),
            SqlInstallSteps.I08_Logins(session),
            SqlInstallSteps.I09_Migrations(session),
            SqlInstallSteps.I10_Programmability(session),
            SqlInstallSteps.I11_Seed(session),
            SqlInstallSteps.I12_License(session, licenseVerifier),
            SqlInstallSteps.I13_Administrator(session),
            SqlInstallSteps.I14_DisableMigrator(session),
            Step("I-15", "Copiez binarele și verific hash-urile", ExitCodes.IisServiceOrFileError, (_, _) =>
            {
                var copied = BinaryDeployer.Deploy(session.KitRoot, session.Manifest, Layout, replaceExisting: false);
                return Task.FromResult(StepResult.Done($"{copied} fișiere copiate în versions\\{session.Manifest.Version}"));
            }),
            Step("I-16", "Scriu configurarea, secretele și web.config", ExitCodes.IisServiceOrFileError, (_, _) => Task.FromResult(WriteConfiguration())),
            Step("I-17", "Aplic drepturile NTFS", ExitCodes.IisServiceOrFileError, (_, _) =>
            {
                var identities = ComponentIdentities.For(Answers.Instance, Answers.Sql.AppWindowsAccount);
                NtfsPermissions.ApplyData(Layout, identities);
                NtfsPermissions.ApplyBinaries(Layout, session.Manifest.Version, identities);
                return Task.FromResult(StepResult.Done());
            }),
            Step("I-18", "Configurez IIS (pool, site, legături HTTPS)", ExitCodes.IisServiceOrFileError, (_, _) => Task.FromResult(ConfigureIis())),
            Step("I-19", "Creez serviciul Windows", ExitCodes.IisServiceOrFileError, (_, _) =>
            {
                new WorkerServiceInstaller(processes, windows.ServiceExists).Install(ServiceSpec());
                return Task.FromResult(StepResult.Done($"ErpWorker-{Answers.Instance}"));
            }),
            Step("I-20", "Creez regulile de firewall", ExitCodes.IisServiceOrFileError, (_, _) =>
            {
                new FirewallRules(processes).Apply(Answers.Instance, Answers.Site.HttpsPort, Answers.Site.HttpRedirect);
                return Task.FromResult(StepResult.Done());
            }),
            SqlInstallSteps.I21_AgentJobs(session),
            SqlInstallSteps.I22_FirstBackup(session),
            Step("I-23", "Pornesc aplicația și verific health-check-ul", ExitCodes.HealthCheckFailed, StartAndCheckAsync),
            Step("I-24", "Finalizez instalarea", ExitCodes.IisServiceOrFileError, (_, _) => Task.FromResult(Finish())),
        ];
    }

    private StepResult ReadAnswers(SetupContext context)
    {
        var loaded = AnswersValidator.Load(answersPath);
        foreach (var warning in loaded.Warnings)
        {
            console.Warning($"  {warning.Field}: {warning.Message}");
        }

        if (!loaded.IsValid)
        {
            foreach (var issue in loaded.Errors)
            {
                console.Error($"  {issue.Field}: {issue.Message}");
            }

            return StepResult.Failed("Fișierul de răspunsuri este invalid.", ExitCodes.InvalidAnswersFile);
        }

        session.Answers = loaded.Answers;
        var secrets = new SecretReader(Environment.GetEnvironmentVariable, session.Unattended ? null : new ConsoleSecretInput());
        string? operatorPassword = null;
        if (Answers.Sql.OperatorAuthentication == SqlAuthentication.Sql)
        {
            var fromEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SecretReader.OperatorPasswordVariable));
            var secret = secrets.ReadOperatorSqlPassword(fromEnvironment || session.Unattended);
            if (secret.Outcome != SecretOutcome.Provided)
            {
                return StepResult.Failed(secret.Message ?? "Parola SQL a operatorului lipsește.", secret.ExitCode);
            }

            operatorPassword = secret.Value;
        }

        session.OperatorPassword = operatorPassword;
        session.Sql = new SqlOperator(session.Target(CheckMode.Install).Sql, operatorPassword, context.Log);

        InstanceState? existing;
        try
        {
            existing = InstanceStateStore.Read(session.StatePath);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            // Un instance.json ilizibil inseamna o instanta existenta: C-23 refuza instalarea.
            context.Log.Warning(ex, "{Path} nu poate fi citit", session.StatePath);
            session.ExistingInstance = new InstanceState
            {
                Instance = Answers.Instance,
                State = InstanceLifecycle.Failed,
                InstallRoot = Answers.InstallRoot,
                DataRoot = Answers.DataRoot,
            };
            existing = null;
        }

        session.ExistingInstance ??= existing;
        if (existing is { State: InstanceLifecycle.Installing } && existing.Instance == Answers.Instance)
        {
            session.State = existing;
            session.InstallId = existing.InstallId;
            session.Resuming = true;
            session.CertificateThumbprint = existing.CertificateThumbprint;
            console.Warning($"Instalarea întreruptă a instanței {Answers.Instance} se reia (pași terminați: {string.Join(", ", existing.CompletedSteps)}).");
        }

        var admin = secrets.ReadInitialAdminPassword(session.Unattended, Answers.Administrator.UserName);
        switch (admin.Outcome)
        {
            case SecretOutcome.Provided:
                session.AdminPassword = admin.Value;
                break;
            case SecretOutcome.Generate:
                session.AdminPassword = null;
                break;
            default:
                return StepResult.Failed(admin.Message ?? "Parola administratorului nu a fost acceptată.", admin.ExitCode);
        }

        return StepResult.Done($"instanța {Answers.Instance}, baza {Answers.Sql.DatabaseName} pe {Answers.Sql.Server}");
    }

    private async Task<StepResult> CheckRequirementsAsync(SetupContext context, CancellationToken ct)
    {
        using var network = new HttpNetworkProbe();
        var checker = new RequirementsChecker(windows, new SqlServerConnector(), network, licenseVerifier, time);
        var report = await checker.RunAsync(session.Target(CheckMode.Install), session.Manifest, session.ExistingInstance, OperatorPassword(), ct);
        foreach (var result in report.Results)
        {
            context.Log.Information("[{Id}] {Status} {Title}: {Message}", result.Id, result.Status, result.Title, result.Message);
        }

        session.Requirements = report;
        RequirementsReportPrinter.Print(console, report);
        if (report.HasFailures)
        {
            return StepResult.Failed("Cerințele nu sunt îndeplinite.", report.ExitCode);
        }

        console.Info(string.Empty);
        console.Info($"Se instalează ERP {session.Manifest.Version}, instanța {Answers.Instance}: https://{Answers.Site.HostName}:{Answers.Site.HttpsPort}/, " +
            $"baza {Answers.Sql.DatabaseName} pe {Answers.Sql.Server}, backup în {Answers.Backup.BackupRoot}.");
        if (!session.Unattended && !session.AssumeYes && !prompt.Confirm(report.HasWarnings ? "Există avertismente. Continuați instalarea?" : "Continuați instalarea?"))
        {
            return StepResult.Failed("Anulat de operator.", ExitCodes.CancelledByOperator);
        }

        return report.HasWarnings ? StepResult.Warning("cerințe îndeplinite, cu avertismente") : StepResult.Done("cerințe îndeplinite");
    }

    private string? OperatorPassword() => session.OperatorPassword;

    private StepResult InstallPrerequisites(IProcessRunner processes)
    {
        var installer = new PrerequisitesInstaller(processes);
        var installed = new List<string>();
        var features = windows.GetInstalledWindowsFeatures() ?? throw new WindowsSetupException("Funcțiile Windows nu au putut fi interogate.");
        var missing = RequirementsChecker.IisFeatures.Where(f => !features.Contains(f)).ToList();
        if (missing.Count > 0 && Answers.Prerequisites.InstallIisFeatures)
        {
            if (installer.InstallIisFeatures(missing) == PrerequisiteResult.RestartRequired)
            {
                return StepResult.Failed("Reporniți serverul și rulați din nou.", ExitCodes.RequirementsNotMet);
            }

            installed.Add("roluri IIS");
        }

        var required = Version.Parse(session.Manifest.HostingBundleVersion);
        var bundleOk = windows.GetAspNetCoreRuntimeVersions().Any(v => v.Major == required.Major && v >= required) && windows.IsAspNetCoreModuleInstalled();
        if (!bundleOk && Answers.Prerequisites.InstallHostingBundle)
        {
            if (installer.InstallHostingBundle(session.KitRoot) == PrerequisiteResult.RestartRequired)
            {
                return StepResult.Failed("Reporniți serverul și rulați din nou.", ExitCodes.RequirementsNotMet);
            }

            installed.Add("Hosting Bundle");
        }

        return installed.Count == 0 ? StepResult.Skipped("cerințele sunt deja instalate") : StepResult.Done($"instalat: {string.Join(", ", installed)}");
    }

    private StepResult WriteConfiguration()
    {
        var sql = Answers.Sql;
        var values = new InstallationSettingsValues(Answers.Instance, session.Manifest.Version,
            AppSettingsGenerator.PublicOrigin(Answers.Site.HostName, Answers.Site.HttpsPort), Layout.Data.InstanceDirectory, sql.Server, sql.DatabaseName,
            sql.AppAuthentication.ToString(), sql.AppAuthentication == SqlAuthentication.Sql ? session.State?.AppLogin : null,
            sql.Encrypt.ToString(), sql.TrustServerCertificate);
        var secrets = session.AppPassword is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [Erp.Configuration.InstallationSecrets.DatabasePasswordKey] = session.AppPassword };
        ConfigurationWriter.Write(session.KitRoot, Layout, session.Manifest.Version, values, secrets);
        return StepResult.Done(Layout.Data.AppSettings);
    }

    private StepResult ConfigureIis()
    {
        var thumbprint = Answers.Site.CertificateThumbprint ?? session.CertificateThumbprint;
        string? generated = null;
        if (thumbprint is null)
        {
            thumbprint = generated = SelfSignedCertificate.CreateInMachineStore(Answers.Site.HostName, time.GetUtcNow());
            session.CertificateThumbprint = thumbprint;
        }

        session.UpdateState(state => state with { CertificateThumbprint = thumbprint });
        IisConfigurator.Apply(new IisSiteSpec(Answers.Instance, Layout.HostDirectory(session.Manifest.Version), Answers.Site.HostName,
            Answers.Site.HttpsPort, Answers.Site.HttpRedirect, thumbprint, Answers.Sql.AppWindowsAccount));
        return generated is null
            ? StepResult.Done($"ERP-{Answers.Instance}")
            : StepResult.Warning($"certificat autosemnat generat ({generated}); folosiți-l doar pentru test");
    }

    private WorkerServiceSpec ServiceSpec() => new(Answers.Instance, Path.Combine(Layout.WorkerDirectory(session.Manifest.Version), "Erp.Worker.exe"),
        Layout.Data.InstanceDirectory, Answers.Sql.AppWindowsAccount);

    private async Task<StepResult> StartAndCheckAsync(SetupContext context, CancellationToken ct)
    {
        ServiceControl.Start($"ErpWorker-{Answers.Instance}", TimeSpan.FromSeconds(60));
        IisConfigurator.EnsureStarted(new IisSiteSpec(Answers.Instance, Layout.HostDirectory(session.Manifest.Version), Answers.Site.HostName,
            Answers.Site.HttpsPort, Answers.Site.HttpRedirect, session.State?.CertificateThumbprint ?? string.Empty));

        var pinned = Answers.Site.AllowSelfSignedCertificate && Answers.Site.CertificateThumbprint is null ? session.State?.CertificateThumbprint : null;
        using var health = new InstallerHealthCheck(Answers.Site.HostName, Answers.Site.HttpsPort, pinned, time);
        var outcome = await health.WaitUntilHealthyAsync(session.Manifest.Version, session.Manifest.LastMigration, ct);
        if (outcome.Succeeded)
        {
            return StepResult.Done(outcome.Message);
        }

        foreach (var (component, folder) in new[] { ("Host", Layout.Data.HostLogs), ("Worker", Layout.Data.WorkerLogs) })
        {
            console.Error($"  Ultimele rânduri din jurnalul {component}:");
            foreach (var line in LogTail.Read(folder))
            {
                console.Error($"    {line}");
            }
        }

        return StepResult.Failed($"Health-check eșuat: {outcome.Message}", ExitCodes.HealthCheckFailed);
    }

    private StepResult Finish()
    {
        session.UpdateState(state => state with
        {
            State = InstanceLifecycle.Installed,
            CurrentVersion = session.Manifest.Version,
            InstalledAt = time.GetUtcNow(),
            LastMigration = session.Manifest.LastMigration,
        });

        var url = AppSettingsGenerator.PublicOrigin(Answers.Site.HostName, Answers.Site.HttpsPort) + "/";
        var administrator = session.Administrator;
        var summary = InitialAdminSummary.Render(url, Answers.Administrator.UserName, administrator?.TemporaryPassword);
        // Singura afisare a parolei (R6): doar pe ecran, niciodata in jurnal.
        console.Info(string.Empty);
        console.Info(summary);

        if (session.Unattended && administrator is { Created: true, PasswordGenerated: true })
        {
            var path = InitialAdminFile.PathFor(Answers.DataRoot, Answers.Instance);
            InitialAdminFile.Write(path, summary);
            InitialAdminFile.GrantDelete(path, ComponentIdentities.For(Answers.Instance, Answers.Sql.AppWindowsAccount).HostPool);
            console.Warning($"Parola inițială a fost scrisă și în {path} (Administrators, SYSTEM); se șterge după prima schimbare a parolei.");
        }

        console.Info($"Pașii următori: autentificare la {url}, asistentul de configurare, copierea backup-urilor din {Answers.Backup.BackupRoot} în afara serverului.");
        return StepResult.Done($"instalarea {Answers.Instance} {session.Manifest.Version} s-a încheiat");
    }

    private static PlannedStep Step(string id, string title, int exitCode, Func<SetupContext, CancellationToken, Task<StepResult>> execute) =>
        new(new DelegateStep(id, title, execute), exitCode);
}
