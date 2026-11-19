using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Engine;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;

namespace Erp.Setup.Presentation;

/// <summary>Rezultatul butonului „Testează conexiunea” (ecranul 1).</summary>
public sealed record SqlConnectionTest(bool Succeeded, string Message, SqlServerInfo? Server = null, DatabaseInfo? Database = null,
    bool TrustedServerCertificate = false, bool IsRemote = false, string? AgentStatus = null);

/// <summary>Un certificat din LocalMachine\My valabil pentru adresa aleasa (ecranul 2).</summary>
public sealed record CertificateChoice(string Thumbprint, string Subject, DateTimeOffset NotAfter)
{
    public string Display => $"{Subject} · expiră {NotAfter:dd.MM.yyyy} · {Thumbprint[..8]}…";
}

/// <summary>Ce ramane dupa plan pentru ecranul 6 (parola exista doar in memorie, 5.10).</summary>
public sealed record InstallOutcome(
    int ExitCode,
    string? FailedStep,
    string? Message,
    string ApplicationUrl,
    string? AdministratorUserName,
    string? TemporaryPassword,
    string? InternalCertificatePath,
    bool RestartRequired);

/// <summary>
/// Tot ce face fereastra pe calculator: citiri, verificari, planul de instalare, cheia de recuperare. Implementarea reala e
/// <see cref="SetupBackend"/>; testele folosesc una simulata.
/// </summary>
public interface ISetupBackend
{
    KitManifest Manifest { get; }

    /// <summary>„DOMENIU\utilizator” (ecranul 1, autentificarea afisata).</summary>
    string CurrentUser { get; }

    /// <summary>Numele complet al calculatorului, adresa implicita (ecranul 2).</summary>
    string DefaultHostName { get; }

    string DataRoot { get; }

    IReadOnlyList<InstanceState> FindInstallations();

    Task<SqlConnectionTest> TestSqlConnectionAsync(string server, string database, CancellationToken ct);

    IReadOnlyList<CertificateChoice> FindCertificates(string hostName);

    LicenseReadResult ReadLicense(string path);

    Task<RequirementsReport> CheckAsync(InstallRequest request, CheckMode mode, CancellationToken ct);

    Task<InstallOutcome> InstallAsync(InstallRequest request, RequirementsReport report, IStepReporter reporter, CancellationToken ct);

    /// <summary>„Recuperează” (11.2); fara cheie, certificatul instalarii e nou si datele criptate anterior se pierd.</summary>
    Task<InstallOutcome> RecoverAsync(InstallRequest request, RequirementsReport report, RecoveryKeyInput? recoveryKey, IStepReporter reporter,
        CancellationToken ct);

    /// <summary>„Actualizează” (10), cu revenire automata.</summary>
    Task<UpgradeOutcome> UpgradeAsync(string instance, IStepReporter reporter, CancellationToken ct);

    /// <summary>„Revino la versiunea anterioară” dupa o actualizare intrerupta (10.2).</summary>
    Task<UpgradeOutcome> RollbackAsync(string instance, IStepReporter reporter, CancellationToken ct);

    /// <summary>„Repară” (11.1); <paramref name="licensePath"/> = „Încarcă altă licență…”.</summary>
    Task<InstallOutcome> RepairAsync(string instance, string? licensePath, IStepReporter reporter, CancellationToken ct);

    /// <summary>„Resetează accesul administratorului” (11.3): parola temporara noua.</summary>
    Task<string> ResetAdministratorAsync(string instance, CancellationToken ct);

    /// <summary>„Pachet de diagnostic” (11.5): calea ZIP-ului.</summary>
    Task<string> DiagnoseAsync(string instance, CancellationToken ct);

    /// <summary>„Dezinstalează” (11.6): ce s-a sters si ce a ramas.</summary>
    Task<(IReadOnlyList<string> Removed, IReadOnlyList<string> Kept)> UninstallAsync(string instance, CancellationToken ct);

    void ExportRecoveryKey(string instance, string pfxPath, string password);

    RecoveryKeyCheck VerifyRecoveryKey(string instance, string pfxPath, string password);

    void WriteAnswers(string path, InstallRequest request);
}

/// <summary>Dialogurile ferestrei; implementarea WPF sta in Erp.Setup.</summary>
public interface ISetupDialogs
{
    string? AskOpenLicense();

    string? AskOpenAnswers();

    string? AskSaveAnswers(string suggestedFileName);

    string? AskSaveRecoveryKey(string suggestedFileName);

    string? AskOpenRecoveryKey();

    void CopyToClipboard(string text);

    void Open(string pathOrUrl);

    void ShowError(string title, string message);
}
