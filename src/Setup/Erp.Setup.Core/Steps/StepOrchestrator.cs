using System.Diagnostics;
using System.Globalization;

namespace Erp.Setup.Core.Steps;

/// <summary>Un pas din plan, cu codul de iesire implicit la esec si comportamentul la reluare.</summary>
public sealed record PlannedStep(IInstallStep Step, int FailureExitCode, bool SkipWhenCompleted = false);

/// <summary>Afisarea progresului (6.6): fiecare pas pe un rand, avertismentele in galben, erorile in rosu.</summary>
public interface IStepReporter
{
    void Started(IInstallStep step);

    void Finished(IInstallStep step, StepResult result, TimeSpan elapsed);
}

/// <summary>Persistarea pasilor terminati (instance.json, upgrade-state.json).</summary>
public interface IStepJournal
{
    bool IsCompleted(string stepId);

    void MarkCompleted(string stepId);
}

public sealed record OrchestrationResult(int ExitCode, IReadOnlyList<(string StepId, StepResult Result)> Steps)
{
    public bool Succeeded => ExitCode is ExitCodes.Success or ExitCodes.SuccessWithWarnings;

    public string? FailedStep => Steps.LastOrDefault(s => s.Result.Outcome == StepOutcome.Failed).StepId;
}

/// <summary>
/// Orchestratorul (5): ruleaza pasii in ordine, scrie inceputul si sfarsitul fiecaruia in jurnal si in starea persistata
/// si se opreste la primul Failed. Pasii sunt idempotenti (R8); la reluare se reruleaza (verificare), cu exceptia celor
/// marcati SkipWhenCompleted. Exceptiile cu cod de iesire (ExitCode) devin esecul pasului.
/// </summary>
public sealed class StepOrchestrator(IStepReporter reporter, IStepJournal? journal = null)
{
    public async Task<OrchestrationResult> RunAsync(IReadOnlyList<PlannedStep> plan, SetupContext context, CancellationToken ct)
    {
        var results = new List<(string, StepResult)>();
        var warnings = false;
        foreach (var planned in plan)
        {
            var step = planned.Step;
            if (planned.SkipWhenCompleted && journal?.IsCompleted(step.Id) == true)
            {
                var skipped = StepResult.Skipped("terminat la rularea anterioară");
                reporter.Finished(step, skipped, TimeSpan.Zero);
                results.Add((step.Id, skipped));
                continue;
            }

            context.Log.Information("[{StepId}] Început: {Title}", step.Id, step.Title);
            reporter.Started(step);
            var watch = Stopwatch.StartNew();
            StepResult result;
            try
            {
                result = await step.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                result = StepResult.Failed("Anulat de operator.", ExitCodes.CancelledByOperator);
            }
            catch (Exception ex)
            {
                context.Log.Error(ex, "[{StepId}] Excepție", step.Id);
                result = StepResult.Failed(ex.Message, ExitCodeOf(ex));
            }

            watch.Stop();
            context.Log.Information("[{StepId}] Sfârșit: {Outcome} {Message} ({ElapsedMs} ms)", step.Id, result.Outcome, result.Message, watch.ElapsedMilliseconds);
            reporter.Finished(step, result, watch.Elapsed);
            results.Add((step.Id, result));

            if (result.Outcome == StepOutcome.Failed)
            {
                return new OrchestrationResult(result.ExitCode ?? planned.FailureExitCode, results);
            }

            warnings |= result.Outcome == StepOutcome.Warning;
            journal?.MarkCompleted(step.Id);
        }

        return new OrchestrationResult(warnings ? ExitCodes.SuccessWithWarnings : ExitCodes.Success, results);
    }

    private static int? ExitCodeOf(Exception ex) =>
        ex.GetType().GetProperty("ExitCode")?.GetValue(ex) is int code ? code : null;

    public static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds < 1 ? "<1 s" : string.Create(CultureInfo.InvariantCulture, $"{elapsed.TotalSeconds:F0} s");

    /// <summary>Formatul din 6.6: <c>[I-07] Rulez migrațiile bazei de date... OK (42 scripturi, 18 s)</c>.</summary>
    public static string FormatFinished(IInstallStep step, StepResult result, TimeSpan elapsed)
    {
        var details = result.Message is null ? FormatElapsed(elapsed) : $"{result.Message}, {FormatElapsed(elapsed)}";
        var label = result.Outcome switch
        {
            StepOutcome.Done => "OK",
            StepOutcome.Skipped => "Sărit",
            StepOutcome.Warning => "Avertisment",
            _ => "EȘEC",
        };
        return $"[{step.Id}] {step.Title}... {label} ({details})";
    }
}

/// <summary>Pas definit printr-o functie; pasii installer-ului sunt compozitii mici ale serviciilor din T5–T12.</summary>
public sealed class DelegateStep(string id, string title, Func<SetupContext, CancellationToken, Task<StepResult>> execute) : IInstallStep
{
    public string Id => id;

    public string Title => title;

    public Task<StepResult> ExecuteAsync(SetupContext context, CancellationToken ct) => execute(context, ct);

    public Task CompensateAsync(SetupContext context, CancellationToken ct) => Task.CompletedTask;
}
