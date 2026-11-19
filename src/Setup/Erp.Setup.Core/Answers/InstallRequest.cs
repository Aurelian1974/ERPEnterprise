namespace Erp.Setup.Core.Answers;

/// <summary>Identitatea aplicatiei pe SQL Server (08, 5.8, D21).</summary>
public enum AppIdentity
{
    /// <summary>SQL pe acelasi calculator: conturile virtuale ale pool-ului si serviciului.</summary>
    VirtualAccounts,

    /// <summary>SQL pe alt calculator, cu un cont gMSA (recomandat).</summary>
    Gmsa,

    /// <summary>SQL pe alt calculator, cu contul calculatorului (avertisment C-15).</summary>
    ComputerAccount,
}

public enum CertificateKind
{
    /// <summary>Certificatul HTTPS intern, generat de installer (implicit, D9).</summary>
    Internal,

    /// <summary>Un certificat existent din LocalMachine\My.</summary>
    Existing,
}

public sealed record SqlRequest(
    string Server,
    string Database,
    string? DataFolder,
    string? LogFolder,
    AppIdentity AppIdentity,
    string? GmsaAccount);

public sealed record SiteRequest(
    string HostName,
    int HttpsPort,
    bool HttpRedirect,
    CertificateKind Certificate,
    string? CertificateThumbprint);

/// <param name="Folder">null = folderul implicit de backup al instantei SQL + numele bazei (08, 5.3).</param>
public sealed record BackupRequest(
    string? Folder,
    TimeOnly FullBackupTime,
    int LogBackupEveryMinutes,
    int RetentionDays,
    DayOfWeek IntegrityCheckDay,
    TimeOnly IntegrityCheckTime);

/// <summary>
/// Ce se instaleaza (08, 5.2, R13): construit de fereastra (din ecrane) sau de linia de comanda (din fisierul de raspunsuri),
/// apoi transformat in plan de acelasi PlanBuilder. Nu contine secrete: nu exista parole SQL (D3).
/// </summary>
public sealed record InstallRequest(
    string Instance,
    string InstallRoot,
    SqlRequest Sql,
    SiteRequest Site,
    BackupRequest Backup,
    string LicenseFile,
    IReadOnlySet<string> AcceptedWarnings)
{
    public const string DefaultInstance = "PROD";
    public const string DefaultDatabase = "ERP_PROD";
    public const string DefaultInstallRoot = @"C:\Program Files\Erp";

    /// <summary>Utilizatorul creat la instalare (08, 5.10): fix, doar litere mici.</summary>
    public const string AdministratorUserName = "administrator";
    public const string AdministratorDisplayName = "Administrator ERP";

    /// <summary>C:\ProgramData\Erp (08, 5.7); testele il pot inlocui.</summary>
    public string DataRoot { get; init; } = DefaultDataRoot;

    public static string DefaultDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Erp");

    public string SiteName => $"ERP-{Instance}";

    public string ServiceName => $"ErpWorker-{Instance}";

    public string InstanceDirectory => Path.Combine(DataRoot, Instance);

    /// <summary>Avertismentul cu acest ID (de exemplu „C-11”) a fost acceptat explicit (08, 6.2).</summary>
    public bool Accepts(string checkId) => AcceptedWarnings.Contains(checkId);
}

public enum IssueSeverity
{
    Error,
    Warning,
}

/// <summary>O problema pe un camp al fisierului de raspunsuri; mesajul e in romana.</summary>
public sealed record AnswersIssue(string Field, string Message, IssueSeverity Severity = IssueSeverity.Error);
