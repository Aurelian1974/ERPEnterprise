using Serilog;

namespace Erp.Setup.Core.Steps;

public interface IInstallStep
{
    /// <summary>De exemplu „I-07”.</summary>
    string Id { get; }

    /// <summary>Text in romana, afisat operatorului.</summary>
    string Title { get; }

    Task<StepResult> ExecuteAsync(SetupContext context, CancellationToken ct);

    /// <summary>Folosit doar la actualizare (10.3).</summary>
    Task CompensateAsync(SetupContext context, CancellationToken ct);
}

public sealed record StepResult(StepOutcome Outcome, string? Message = null);

public enum StepOutcome
{
    Done = 1,
    Skipped = 2,
    Warning = 3,
    Failed = 4,
}

public sealed class SetupContext
{
    public required ILogger Log { get; init; }

    public required string KitRoot { get; init; }

    public required bool Unattended { get; init; }
}
