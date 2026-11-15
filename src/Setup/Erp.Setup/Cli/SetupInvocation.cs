namespace Erp.Setup.Cli;

/// <summary>Comanda si optiunile primite din linia de comanda (6.1). Nu contine secrete (R6).</summary>
public sealed record SetupInvocation
{
    public required string Command { get; init; }

    public string? AnswersPath { get; init; }

    public string? Instance { get; init; }

    public bool Unattended { get; init; }

    public string LogLevel { get; init; } = "Information";

    public bool Yes { get; init; }

    public bool NoColor { get; init; }

    public string? User { get; init; }

    public bool RotateSecrets { get; init; }

    public string? LicensePath { get; init; }

    public string? ConfirmInstance { get; init; }

    public bool RemoveJobs { get; init; }

    public bool RemoveLogins { get; init; }

    public bool DevSkipSignature { get; init; }
}
