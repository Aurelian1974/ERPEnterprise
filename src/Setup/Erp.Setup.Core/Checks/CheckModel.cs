using Erp.Setup.Core.Answers;

namespace Erp.Setup.Core.Checks;

public enum CheckMode
{
    Check,
    Install,
    Upgrade,
    Repair,
    Recover,
}

public enum CheckStatus
{
    Passed,
    Warning,
    Failed,
    NotApplicable,
}

/// <summary>Rezultatul unei verificari C-xx (08, 5.4). Mesajul e in romana si nu contine secrete.</summary>
public sealed record CheckResult(string Id, string Title, CheckStatus Status, string Message, int? ExitCode = null);

/// <summary>Ce se verifica (08, 5.4): din cerere (check/install) sau din instalarea existenta (upgrade/repair).</summary>
public sealed record CheckTarget
{
    public required CheckMode Mode { get; init; }

    public required string Instance { get; init; }

    public required string InstallRoot { get; init; }

    public required string DataRoot { get; init; }

    public required string HostName { get; init; }

    public required int HttpsPort { get; init; }

    public bool HttpRedirect { get; init; }

    public CertificateKind Certificate { get; init; } = CertificateKind.Internal;

    public string? CertificateThumbprint { get; init; }

    public required SqlTarget Sql { get; init; }

    public AppIdentity AppIdentity { get; init; } = AppIdentity.VirtualAccounts;

    public string? GmsaAccount { get; init; }

    public string? DataFolder { get; init; }

    public string? LogFolder { get; init; }

    /// <summary>null = folderul implicit de backup al instantei SQL + numele bazei.</summary>
    public string? BackupFolder { get; init; }

    public required string LicenseFile { get; init; }

    public string SiteName => $"ERP-{Instance}";

    public string ServiceName => $"ErpWorker-{Instance}";

    public static CheckTarget From(InstallRequest request, CheckMode mode) => new()
    {
        Mode = mode,
        Instance = request.Instance,
        InstallRoot = request.InstallRoot,
        DataRoot = request.DataRoot,
        HostName = request.Site.HostName,
        HttpsPort = request.Site.HttpsPort,
        HttpRedirect = request.Site.HttpRedirect,
        Certificate = request.Site.Certificate,
        CertificateThumbprint = request.Site.CertificateThumbprint,
        Sql = new SqlTarget(request.Sql.Server, request.Sql.Database, request.Instance),
        AppIdentity = request.Sql.AppIdentity,
        GmsaAccount = request.Sql.GmsaAccount,
        DataFolder = request.Sql.DataFolder,
        LogFolder = request.Sql.LogFolder,
        BackupFolder = request.Backup.Folder,
        LicenseFile = request.LicenseFile,
    };
}

/// <summary>Conexiunea operatorului la SQL Server, numai cu Windows authentication (R5).</summary>
/// <param name="TrustServerCertificate">Doar dupa avertismentul C-10 acceptat: serverul SQL nu are un certificat de incredere.</param>
public sealed record SqlTarget(string Server, string DatabaseName, string Instance, bool TrustServerCertificate = false);
