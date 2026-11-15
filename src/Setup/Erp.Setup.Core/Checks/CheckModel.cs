using Erp.Setup.Core.Answers;

namespace Erp.Setup.Core.Checks;

public enum CheckMode
{
    Check,
    Install,
    Upgrade,
    Repair,
}

public enum CheckStatus
{
    Passed,
    Warning,
    Failed,
    NotApplicable,
}

/// <summary>Rezultatul unei verificari C-xx (7). Mesajul e in romana si nu contine secrete.</summary>
public sealed record CheckResult(string Id, string Title, CheckStatus Status, string Message, int? ExitCode = null);

/// <summary>Ce se verifica: provine din fisierul de raspunsuri (check/install) sau din instalare (upgrade/repair).</summary>
public sealed record CheckTarget
{
    public required CheckMode Mode { get; init; }

    public required string Instance { get; init; }

    public required string InstallRoot { get; init; }

    public required string DataRoot { get; init; }

    public required string HostName { get; init; }

    public required int HttpsPort { get; init; }

    public bool HttpRedirect { get; init; }

    public string? CertificateThumbprint { get; init; }

    public bool AllowSelfSignedCertificate { get; init; }

    public required SqlTarget Sql { get; init; }

    public string? DataFilePath { get; init; }

    public string? LogFilePath { get; init; }

    public int InitialDataSizeMb { get; init; } = 1024;

    public required string BackupRoot { get; init; }

    public required string LicenseFilePath { get; init; }

    public bool InstallIisFeatures { get; init; } = true;

    public bool InstallHostingBundle { get; init; } = true;

    public string SiteName => $"ERP-{Instance}";

    public string ServiceName => $"ErpWorker-{Instance}";

    public static CheckTarget FromAnswers(InstallAnswers answers, CheckMode mode) => new()
    {
        Mode = mode,
        Instance = answers.Instance,
        InstallRoot = answers.InstallRoot,
        DataRoot = answers.DataRoot,
        HostName = answers.Site.HostName,
        HttpsPort = answers.Site.HttpsPort,
        HttpRedirect = answers.Site.HttpRedirect,
        CertificateThumbprint = answers.Site.CertificateThumbprint,
        AllowSelfSignedCertificate = answers.Site.AllowSelfSignedCertificate,
        Sql = new SqlTarget(answers.Sql.Server, answers.Sql.DatabaseName, answers.Sql.OperatorAuthentication,
            answers.Sql.OperatorSqlLogin, answers.Sql.Encrypt, answers.Sql.TrustServerCertificate, answers.Instance),
        DataFilePath = answers.Sql.DataFilePath,
        LogFilePath = answers.Sql.LogFilePath,
        InitialDataSizeMb = answers.Sql.InitialDataSizeMb,
        BackupRoot = answers.Backup.BackupRoot,
        LicenseFilePath = answers.LicenseFilePath,
        InstallIisFeatures = answers.Prerequisites.InstallIisFeatures,
        InstallHostingBundle = answers.Prerequisites.InstallHostingBundle,
    };
}

/// <summary>Conexiunea operatorului la SQL Server; parola nu face parte din tinta (R6).</summary>
public sealed record SqlTarget(
    string Server,
    string DatabaseName,
    SqlAuthentication OperatorAuthentication,
    string? OperatorSqlLogin,
    SqlEncrypt Encrypt,
    bool TrustServerCertificate,
    string Instance);
