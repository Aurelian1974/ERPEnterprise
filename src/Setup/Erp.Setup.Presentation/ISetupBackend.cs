using Erp.Licensing;
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

    Task<RequirementsReport> CheckAsync(InstallRequest request, CancellationToken ct);

    Task<InstallOutcome> InstallAsync(InstallRequest request, RequirementsReport report, IStepReporter reporter, CancellationToken ct);

    void ExportRecoveryKey(InstallRequest request, string pfxPath, string password);

    void WriteAnswers(string path, InstallRequest request);
}

/// <summary>Dialogurile ferestrei; implementarea WPF sta in Erp.Setup.</summary>
public interface ISetupDialogs
{
    string? AskOpenLicense();

    string? AskOpenAnswers();

    string? AskSaveAnswers(string suggestedFileName);

    string? AskSaveRecoveryKey(string suggestedFileName);

    void CopyToClipboard(string text);

    void Open(string pathOrUrl);

    void ShowError(string title, string message);
}
