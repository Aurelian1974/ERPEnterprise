namespace Erp.Setup.Cli;

/// <summary>Comanda si optiunile primite din linia de comanda (08, 6.1). Nu contine secrete: parolele nu se dau ca argumente.</summary>
public sealed record SetupInvocation
{
    public required string Command { get; init; }

    public string? AnswersPath { get; init; }

    public string? Instance { get; init; }

    public bool Unattended { get; init; }

    /// <summary>install: unde se exporta cheia de recuperare (obligatoriu cu --unattended).</summary>
    public string? RecoveryKeyOut { get; init; }

    /// <summary>recover, verify-recovery-key: fisierul .pfx existent.</summary>
    public string? RecoveryKeyPath { get; init; }

    /// <summary>export-recovery-key: fisierul .pfx; diagnose: folderul pachetului.</summary>
    public string? OutPath { get; init; }

    public string? LicensePath { get; init; }

    public string? ConfirmInstance { get; init; }

    public string LogLevel { get; init; } = "Information";

    public bool NoColor { get; init; }

    public bool DevSkipSignature { get; init; }
}
