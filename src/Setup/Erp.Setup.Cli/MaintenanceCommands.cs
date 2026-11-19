using System.Runtime.Versioning;
using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Secrets;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Engine;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Probes;
using Erp.Setup.Windows.Probes;

namespace Erp.Setup.Cli;

/// <summary>Comenzile repair, recover, reset-admin, diagnose si uninstall (08, 6.1, 11).</summary>
[SupportedOSPlatform("windows")]
internal sealed class MaintenanceCommands(CommandContext context, Action<string> moveLogsTo)
{
    private readonly OperatorConsole console = context.Console;
    private readonly SetupInvocation invocation = context.Invocation;

    /// <summary>„Repară” (11.1): aceeasi versiune; reface tot ce tine de calculator si de bază, fara utilizatori si date.</summary>
    public async Task<int> RepairAsync(CancellationToken ct)
    {
        var (request, state) = UpgradeSession.LoadInstallation(InstallRequest.DefaultDataRoot, invocation.Instance!);
        var report = await ChecksAsync(request, CheckMode.Repair, state, ct).ConfigureAwait(false);
        if (report.HasFailures)
        {
            console.Error($"Cerințele nu sunt îndeplinite (cod {report.ExitCode}).");
            return report.ExitCode;
        }

        var session = InstallSession.StartRepair(request, context.Manifest, context.KitRoot, Sql(request, report.TrustServerCertificate), state, report,
            invocation.LicensePath is { } license ? Path.GetFullPath(license) : null);
        return await RunPlanAsync(session, "Repararea", ct).ConfigureAwait(false);
    }

    /// <summary>„Recuperează o instalare existentă” (11.2) cu cheia de recuperare; parola se cere cu ecou ascuns.</summary>
    public async Task<int> RecoverAsync(CancellationToken ct)
    {
        var (request, exitCode) = CheckCommand.LoadRequest(context);
        if (request is null)
        {
            return exitCode;
        }

        var password = new ConsoleSecretInput().ReadHidden("Parola cheii de recuperare: ");
        if (password is null)
        {
            return ExitCodes.CancelledByOperator;
        }

        var key = Path.GetFullPath(invocation.RecoveryKeyPath!);
        if (new WindowsSetup(context.Log, context.Time).VerifyRecoveryKey(key, password, expectedThumbprint: null) is var check and not RecoveryKeyCheck.Valid)
        {
            console.Error($"{RecoveryKeyPolicy.Message(check)} (cod {ExitCodes.RecoveryKeyInvalid})");
            return ExitCodes.RecoveryKeyInvalid;
        }

        var report = await ChecksAsync(request, CheckMode.Recover, existing: null, ct).ConfigureAwait(false);
        if (report.HasFailures)
        {
            console.Error($"Cerințele nu sunt îndeplinite (cod {report.ExitCode}).");
            return report.ExitCode;
        }

        var session = InstallSession.StartRecover(request, context.Manifest, context.KitRoot, Sql(request, report.TrustServerCertificate), report,
            new RecoveryKeyInput(key, password));
        return await RunPlanAsync(session, "Recuperarea", ct).ConfigureAwait(false);
    }

    /// <summary>„Resetează accesul administratorului” (11.3): parola noua se afiseaza o singura data, fara varianta neasistata.</summary>
    public async Task<int> ResetAdminAsync(CancellationToken ct)
    {
        var (request, state) = UpgradeSession.LoadInstallation(InstallRequest.DefaultDataRoot, invocation.Instance!);
        if (!Ask($"Resetez accesul administratorului instalării {request.Instance}? Sesiunile lui se închid, iar 2FA se reactivează la autentificare."))
        {
            return ExitCodes.CancelledByOperator;
        }

        var password = await AdministratorAccess.ResetAsync(state, Sql(request, state.TrustServerCertificate), context.Manifest.Version, ct)
            .ConfigureAwait(false);
        var url = Core.Deployment.AppSettingsGenerator.PublicOrigin(request.Site.HostName, request.Site.HttpsPort) + "/";
        console.Info(string.Empty);
        console.Info(InitialAdminSummary.Render(url, InstallRequest.AdministratorUserName, password));
        return ExitCodes.Success;
    }

    public async Task<int> DiagnoseAsync(CancellationToken ct)
    {
        var (request, state) = UpgradeSession.LoadInstallation(InstallRequest.DefaultDataRoot, invocation.Instance!);
        var folder = invocation.OutPath is { } output ? Path.GetFullPath(output) : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = await DiagnosticPackage.CreateAsync(request, state, Sql(request, state.TrustServerCertificate), new WindowsSetup(context.Log, context.Time),
            folder, context.Time, ct).ConfigureAwait(false);
        console.Info($"Pachetul de diagnostic: {path}");
        return ExitCodes.Success;
    }

    /// <summary>„Dezinstalează” (11.6): confirmarea prin numele instalarii (--confirm-instance).</summary>
    public async Task<int> UninstallAsync(CancellationToken ct)
    {
        if (!string.Equals(invocation.ConfirmInstance, invocation.Instance, StringComparison.Ordinal))
        {
            console.Error($"--confirm-instance trebuie să fie exact numele instalării ({invocation.Instance}) (cod {ExitCodes.InvalidCommandLine}).");
            return ExitCodes.InvalidCommandLine;
        }

        var (request, state) = UpgradeSession.LoadInstallation(InstallRequest.DefaultDataRoot, invocation.Instance!);
        moveLogsTo(Erp.Configuration.InstanceDataPaths.For(request.DataRoot, request.Instance).SetupLogs);
        var result = await Uninstaller.RunAsync(request, state, Sql(request, state.TrustServerCertificate), new WindowsSetup(context.Log, context.Time), ct)
            .ConfigureAwait(false);
        console.Info("Șters:");
        result.Removed.ToList().ForEach(r => console.Info($"  - {r}"));
        console.Info("Păstrat (se șterge manual, dacă se dorește):");
        result.Kept.ToList().ForEach(k => console.Info($"  - {k}"));
        return ExitCodes.Success;
    }

    private async Task<int> RunPlanAsync(InstallSession session, string action, CancellationToken ct)
    {
        var windows = new WindowsSetup(context.Log, context.Time);
        var plan = PlanBuilder.Install(session, windows, new LicenseReader(LicenseKeys.Embedded, context.Time), context.Time);
        var setup = new SetupContext { Log = context.Log, KitRoot = context.KitRoot, Unattended = invocation.Unattended };
        var result = await new StepOrchestrator(new ConsoleStepReporter(console), session).RunAsync(plan, setup, ct).ConfigureAwait(false);
        if (session.State is not null)
        {
            moveLogsTo(session.Paths.SetupLogs);
        }

        if (result.Succeeded)
        {
            console.Info($"{action} s-a încheiat.");
        }
        else
        {
            console.Error($"{action} s-a oprit la pasul {result.FailedStep} (cod {result.ExitCode}).");
        }

        return result.ExitCode;
    }

    private async Task<RequirementsReport> ChecksAsync(InstallRequest request, CheckMode mode, InstanceState? existing, CancellationToken ct)
    {
        using var network = new HttpNetworkProbe();
        var checker = new RequirementsChecker(new WindowsProbe(), new SqlServerConnector(), network, new LicenseReader(LicenseKeys.Embedded, context.Time),
            context.Time);
        var report = await checker.RunAsync(CheckTarget.From(request, mode), context.Manifest,
            new KitCheck(true, $"{context.Manifest.Product} {context.Manifest.Version}"), existing, ct).ConfigureAwait(false);
        RequirementsReportPrinter.Print(console, report);
        return report;
    }

    private SqlOperator Sql(InstallRequest request, bool trust) =>
        new(new SqlTarget(request.Sql.Server, request.Sql.Database, request.Instance, trust), context.Log);

    private bool Ask(string question)
    {
        console.Info($"{question} [d/N]");
        var answer = Console.ReadLine()?.Trim();
        return answer is not null && (answer.Equals("d", StringComparison.OrdinalIgnoreCase) || answer.Equals("da", StringComparison.OrdinalIgnoreCase));
    }
}
