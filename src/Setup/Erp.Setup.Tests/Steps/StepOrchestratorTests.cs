using Erp.Setup.Core;
using Erp.Setup.Core.Steps;
using Serilog;

namespace Erp.Setup.Tests.Steps;

/// <summary>Orchestratorul (5, 8.1): ordinea, oprirea la primul esec, codurile de iesire si jurnalul pentru reluare.</summary>
public sealed class StepOrchestratorTests
{
    private readonly List<string> executed = [];
    private readonly Journal journal = new();
    private readonly Reporter reporter = new();

    private static SetupContext Context() => new() { Log = new LoggerConfiguration().CreateLogger(), KitRoot = ".", Unattended = true };

    private PlannedStep Step(string id, StepResult result, int failureExitCode = ExitCodes.SqlInstallError, bool skipWhenCompleted = false) =>
        new(new DelegateStep(id, id, (_, _) =>
        {
            executed.Add(id);
            return Task.FromResult(result);
        }), failureExitCode, skipWhenCompleted);

    private Task<OrchestrationResult> Run(params PlannedStep[] plan) =>
        new StepOrchestrator(reporter, journal).RunAsync(plan, Context(), TestContext.Current.CancellationToken);

    [Fact]
    public async Task Stops_at_first_failure_with_the_step_default_code_and_journals_only_finished_steps()
    {
        var result = await Run(Step("I-06", StepResult.Done()), Step("I-07", StepResult.Failed("x")), Step("I-08", StepResult.Done()));

        Assert.Equal(ExitCodes.SqlInstallError, result.ExitCode);
        Assert.Equal("I-07", result.FailedStep);
        Assert.Equal(["I-06", "I-07"], executed);
        Assert.Equal(["I-06"], journal.Completed);
        Assert.Equal(["I-06", "I-07"], reporter.Finished);
    }

    [Fact]
    public async Task Code_of_the_result_or_of_the_exception_overrides_the_step_default()
    {
        var explicitCode = await Run(Step("I-12", StepResult.Failed("licență", ExitCodes.InvalidLicense)));
        var thrown = await Run(new PlannedStep(new DelegateStep("I-19", "x", (_, _) => throw new CodedException(ExitCodes.IisServiceOrFileError)),
            ExitCodes.SqlInstallError));
        var plain = await Run(new PlannedStep(new DelegateStep("I-20", "x", (_, _) => throw new InvalidOperationException("x")),
            ExitCodes.IisServiceOrFileError));

        Assert.Equal(ExitCodes.InvalidLicense, explicitCode.ExitCode);
        Assert.Equal(ExitCodes.IisServiceOrFileError, thrown.ExitCode);
        Assert.Equal(ExitCodes.IisServiceOrFileError, plain.ExitCode);
    }

    [Fact]
    public async Task Warnings_end_with_code_1_and_skipped_steps_do_not()
    {
        Assert.Equal(ExitCodes.SuccessWithWarnings, (await Run(Step("I-03", StepResult.Warning("w")), Step("I-04", StepResult.Done()))).ExitCode);
        Assert.Equal(ExitCodes.Success, (await Run(Step("I-04", StepResult.Skipped("deja instalat")))).ExitCode);
    }

    [Fact]
    public async Task On_resume_completed_steps_run_again_except_those_marked_skip_when_completed()
    {
        journal.Completed.AddRange(["I-06", "I-22"]);

        var result = await Run(Step("I-06", StepResult.Done()), Step("I-22", StepResult.Done(), skipWhenCompleted: true), Step("I-23", StepResult.Done()));

        Assert.Equal(ExitCodes.Success, result.ExitCode);
        Assert.Equal(["I-06", "I-23"], executed);
        Assert.Equal(StepOutcome.Skipped, result.Steps.Single(s => s.StepId == "I-22").Result.Outcome);
    }

    [Fact]
    public async Task Cancellation_ends_with_code_2()
    {
        using var cts = new CancellationTokenSource();
        var plan = new PlannedStep(new DelegateStep("I-09", "x", (_, ct) =>
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(StepResult.Done());
        }), ExitCodes.SqlInstallError);

        var result = await new StepOrchestrator(reporter, journal).RunAsync([plan], Context(), cts.Token);

        Assert.Equal(ExitCodes.CancelledByOperator, result.ExitCode);
    }

    [Fact]
    public void Console_line_uses_the_documented_format()
    {
        var step = new DelegateStep("I-09", "Rulez migrațiile bazei de date", (_, _) => Task.FromResult(StepResult.Done()));

        Assert.Equal("[I-09] Rulez migrațiile bazei de date... OK (42 scripturi, 18 s)",
            StepOrchestrator.FormatFinished(step, StepResult.Done("42 scripturi"), TimeSpan.FromSeconds(18)));
        Assert.Equal("[I-09] Rulez migrațiile bazei de date... EȘEC (<1 s)",
            StepOrchestrator.FormatFinished(step, new StepResult(StepOutcome.Failed), TimeSpan.Zero));
    }

    private sealed class CodedException(int exitCode) : Exception("eșec")
    {
        public int ExitCode => exitCode;
    }

    private sealed class Journal : IStepJournal
    {
        public List<string> Completed { get; } = [];

        public bool IsCompleted(string stepId) => Completed.Contains(stepId);

        public void MarkCompleted(string stepId) => Completed.Add(stepId);
    }

    private sealed class Reporter : IStepReporter
    {
        public List<string> Finished { get; } = [];

        public void Started(IInstallStep step)
        {
        }

        void IStepReporter.Finished(IInstallStep step, StepResult result, TimeSpan elapsed) => Finished.Add(step.Id);
    }
}
