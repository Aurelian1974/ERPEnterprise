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

/// <param name="ExitCode">Codul de iesire (6.4) cand pasul esueaza; implicit codul pasului din plan.</param>
public sealed record StepResult(StepOutcome Outcome, string? Message = null, int? ExitCode = null)
{
    public static StepResult Done(string? message = null) => new(StepOutcome.Done, message);

    public static StepResult Skipped(string message) => new(StepOutcome.Skipped, message);

    public static StepResult Warning(string message) => new(StepOutcome.Warning, message);

    public static StepResult Failed(string message, int? exitCode = null) => new(StepOutcome.Failed, message, exitCode);
}

public enum StepOutcome
{
    Done = 1,
    Skipped = 2,
    Warning = 3,
    Failed = 4,
}

public sealed class SetupContext
{
    private readonly Dictionary<Type, object> features = [];

    public required ILogger Log { get; init; }

    public required string KitRoot { get; init; }

    public required bool Unattended { get; init; }

    /// <summary>Starea comenzii, partajata intre pasi (de exemplu sesiunea instalarii).</summary>
    public T Feature<T>()
        where T : class =>
        features.TryGetValue(typeof(T), out var value) ? (T)value : throw new InvalidOperationException($"{typeof(T).Name} lipsește din context.");

    public void SetFeature<T>(T value)
        where T : class => features[typeof(T)] = value;
}
