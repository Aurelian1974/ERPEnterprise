using System.Runtime.Versioning;
using Erp.Setup.Cli;
using Erp.Setup.Core;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.Steps;
using Serilog;

namespace Erp.Setup.Install;

/// <summary>Comanda <c>install</c> (8): orchestratorul peste I-01…I-24, cu reluarea unei instalari intrerupte (8.1).</summary>
[SupportedOSPlatform("windows")]
internal sealed class InstallCommand(TimeProvider time)
{
    /// <summary>Folderul logs\setup al instantei, dupa ce I-05 l-a creat; jurnalul installer-ului se muta acolo la final.</summary>
    public string? SetupLogDirectory { get; private set; }

    public async Task<int> RunAsync(SetupInvocation invocation, string kitRoot, KitManifest manifest, ILogger log, OperatorConsole console)
    {
        var session = new InstallSession
        {
            KitRoot = kitRoot,
            Manifest = manifest,
            Unattended = invocation.Unattended,
            AssumeYes = invocation.Yes,
        };
        var context = new SetupContext { Log = log, KitRoot = kitRoot, Unattended = invocation.Unattended };
        context.SetFeature(session);

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        OrchestrationResult result;
        try
        {
            var plan = new InstallPlan(session, invocation.AnswersPath!, console, new ConsolePrompt(console), time).Build(log);
            result = await new StepOrchestrator(new ConsoleStepReporter(console), session).RunAsync(plan, context, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            await DisableMigratorAsync(session, log, console).ConfigureAwait(false);
            if (session.State is not null)
            {
                SetupLogDirectory = Path.Combine(session.State.DataRoot, session.State.Instance, "logs", "setup");
            }
        }

        if (!result.Succeeded)
        {
            log.Error("Instalarea s-a oprit la {Step}, cod {ExitCode}", result.FailedStep, result.ExitCode);
            console.Error($"Instalarea s-a oprit la pasul {result.FailedStep} (cod {result.ExitCode}).");
            if (session.State is not null && result.ExitCode != ExitCodes.CancelledByOperator)
            {
                console.Info("Corectați cauza și rulați din nou aceeași comandă: instalarea se reia de unde a rămas.");
            }
        }

        return result.ExitCode;
    }

    // R4: credentialele de migrare nu raman active nici la esec.
    private static async Task DisableMigratorAsync(InstallSession session, ILogger log, OperatorConsole console)
    {
        try
        {
            await SqlInstallSteps.DisableMigratorIfEnabledAsync(session, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Loginul de migrare nu a putut fi dezactivat");
            console.Error("Loginul de migrare nu a putut fi dezactivat; dezactivați-l manual din SQL Server.");
        }
    }

    private sealed class ConsoleStepReporter(OperatorConsole console) : IStepReporter
    {
        public void Started(IInstallStep step) => console.Info($"[{step.Id}] {step.Title}...");

        public void Finished(IInstallStep step, StepResult result, TimeSpan elapsed)
        {
            var line = $"[{step.Id}] {Label(result.Outcome)} ({StepOrchestrator.FormatElapsed(elapsed)}){(result.Message is null ? string.Empty : ": " + result.Message)}";
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

        private static string Label(StepOutcome outcome) => outcome switch
        {
            StepOutcome.Done => "OK",
            StepOutcome.Skipped => "Sărit",
            StepOutcome.Warning => "Avertisment",
            _ => "EȘEC",
        };
    }

    private sealed class ConsolePrompt(OperatorConsole console) : IOperatorPrompt
    {
        public bool Confirm(string question)
        {
            console.Info($"{question} [d/N]");
            var answer = Console.ReadLine()?.Trim();
            return answer is not null && (answer.Equals("d", StringComparison.OrdinalIgnoreCase) || answer.Equals("da", StringComparison.OrdinalIgnoreCase));
        }
    }
}
