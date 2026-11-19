using System.Runtime.Versioning;
using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Engine;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Probes;
using Erp.Setup.Windows.Probes;

namespace Erp.Setup.Cli;

/// <summary>Comenzile <c>upgrade</c> si <c>rollback</c> (08, 6.1, 10).</summary>
[SupportedOSPlatform("windows")]
internal sealed class UpgradeCommand(CommandContext context, Action<string> moveLogsTo)
{
    private readonly OperatorConsole console = context.Console;

    public async Task<int> UpgradeAsync(CancellationToken ct)
    {
        var (request, state) = UpgradeSession.LoadInstallation(InstallRequest.DefaultDataRoot, context.Invocation.Instance!);
        if (state.State is InstanceLifecycle.Upgrading or InstanceLifecycle.RollingBack)
        {
            console.Error($"Există o actualizare întreruptă; singura comandă permisă e „rollback” (cod {ExitCodes.UnfinishedUpgrade}).");
            return ExitCodes.UnfinishedUpgrade;
        }

        // Verificarile de actualizare (5.4: C-01…C-05, C-07, C-10…C-13, C-17…C-19); U-01 adauga versiunile, licenta si spatiul.
        using var network = new HttpNetworkProbe();
        var checker = new RequirementsChecker(new WindowsProbe(), new SqlServerConnector(), network, new LicenseReader(LicenseKeys.Embedded, context.Time),
            context.Time);
        var report = await checker.RunAsync(CheckTarget.From(request, CheckMode.Upgrade), context.Manifest,
            new KitCheck(true, $"{context.Manifest.Product} {context.Manifest.Version}"), state, ct).ConfigureAwait(false);
        var relevant = report.Results.Where(r => UpgradeChecks.Contains(r.Id)).ToList();
        RequirementsReportPrinter.Print(console, report with { Results = relevant });
        if (relevant.Any(r => r.Status == CheckStatus.Failed))
        {
            var code = relevant.First(r => r.Status == CheckStatus.Failed).ExitCode ?? ExitCodes.RequirementsNotMet;
            console.Error($"Cerințele nu sunt îndeplinite (cod {code}).");
            return code;
        }

        var warnings = relevant.Where(r => r.Status == CheckStatus.Warning && !request.Accepts(r.Id)).ToList();
        if (context.Invocation.Unattended && warnings.Count > 0)
        {
            console.Error($"Avertismente neacceptate în acceptedWarnings: {string.Join(", ", warnings.Select(w => w.Id))} (cod {ExitCodes.RequirementsNotMet}).");
            return ExitCodes.RequirementsNotMet;
        }

        if (!context.Invocation.Unattended && !Ask($"Actualizez {request.Instance} de la {state.CurrentVersion} la {context.Manifest.Version}? " +
            "Utilizatorii văd pagina „Aplicația se actualizează” pe durata actualizării."))
        {
            return ExitCodes.CancelledByOperator;
        }

        var session = UpgradeSession.Start(request, state, context.Manifest, context.KitRoot,
            new SqlOperator(new SqlTarget(request.Sql.Server, request.Sql.Database, request.Instance, state.TrustServerCertificate), context.Log),
            report.BackupFolder);
        var outcome = await Runner(session).RunAsync(Context(), ct).ConfigureAwait(false);
        moveLogsTo(session.Paths.SetupLogs);
        return Report(outcome, state.CurrentVersion);
    }

    public async Task<int> RollbackAsync(CancellationToken ct)
    {
        var (request, state) = UpgradeSession.LoadInstallation(InstallRequest.DefaultDataRoot, context.Invocation.Instance!);
        var session = UpgradeSession.Start(request, state, context.Manifest, context.KitRoot,
            new SqlOperator(new SqlTarget(request.Sql.Server, request.Sql.Database, request.Instance, state.TrustServerCertificate), context.Log), null);
        if (session.Upgrade is null)
        {
            console.Error("Nu există o actualizare întreruptă pentru această instalare.");
            return ExitCodes.InvalidCommandLine;
        }

        var outcome = await Runner(session).RollbackAsync(Context(), ct).ConfigureAwait(false);
        moveLogsTo(session.Paths.SetupLogs);
        return Report(outcome, session.Upgrade?.FromVersion ?? state.CurrentVersion);
    }

    /// <summary>Verificarile rulate la actualizare (5.4, ultimul paragraf).</summary>
    public static readonly IReadOnlySet<string> UpgradeChecks =
        new HashSet<string>(["C-01", "C-02", "C-03", "C-04", "C-05", "C-07", "C-10", "C-11", "C-12", "C-13", "C-17", "C-18", "C-19"]);

    private UpgradeRunner Runner(UpgradeSession session) =>
        new(session, new WindowsSetup(context.Log, context.Time), new LicenseReader(LicenseKeys.Embedded, context.Time), context.Time,
            new ConsoleStepReporter(console));

    private SetupContext Context() => new() { Log = context.Log, KitRoot = context.KitRoot, Unattended = context.Invocation.Unattended };

    private int Report(UpgradeOutcome outcome, string? previousVersion)
    {
        switch (outcome.ExitCode)
        {
            case ExitCodes.Success or ExitCodes.SuccessWithWarnings:
                console.Info($"Actualizare reușită: {context.Manifest.Version}.");
                break;
            case ExitCodes.UpgradeFailedRolledBack:
                console.Error($"Actualizarea nu a reușit ({outcome.FailedStep}: {outcome.Reason}); instalarea a revenit automat la versiunea " +
                    $"{previousVersion} și funcționează (cod {outcome.ExitCode}).");
                break;
            case ExitCodes.UpgradeFailedRollbackFailed:
                console.Error($"Actualizarea și revenirea au eșuat ({outcome.FailedStep}: {outcome.Reason}); instalarea e în mentenanță (cod {outcome.ExitCode}). Pași manuali:");
                foreach (var step in outcome.ManualSteps)
                {
                    console.Error($"  - {step}");
                }

                break;
            default:
                console.Error($"Actualizarea s-a oprit la {outcome.FailedStep}: {outcome.Reason} (cod {outcome.ExitCode}). Nu s-a modificat nimic.");
                break;
        }

        return outcome.ExitCode;
    }

    private bool Ask(string question)
    {
        console.Info($"{question} [d/N]");
        var answer = Console.ReadLine()?.Trim();
        return answer is not null && (answer.Equals("d", StringComparison.OrdinalIgnoreCase) || answer.Equals("da", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Afisarea pasilor in consola (6.6): fiecare pas pe un rand, avertismentele in galben, erorile in rosu.</summary>
internal sealed class ConsoleStepReporter(OperatorConsole console) : IStepReporter
{
    public void Started(IInstallStep step) => console.Info($"[{step.Id}] {step.Title}...");

    public void Finished(IInstallStep step, StepResult result, TimeSpan elapsed)
    {
        var line = StepOrchestrator.FormatFinished(step, result, elapsed);
        switch (result.Outcome)
        {
            case StepOutcome.Warning:
                console.Warning(line);
                break;
            case StepOutcome.Failed:
                console.Error(line);
                break;
            default:
                console.Info(line);
                break;
        }
    }
}
