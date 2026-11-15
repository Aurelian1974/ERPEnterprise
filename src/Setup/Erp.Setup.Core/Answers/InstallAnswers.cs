namespace Erp.Setup.Core.Answers;

/// <summary>Raspunsurile validate, cu valorile implicite din 6.2 aplicate.</summary>
public sealed record InstallAnswers(
    string Instance,
    string InstallRoot,
    string DataRoot,
    SiteAnswers Site,
    SqlAnswers Sql,
    BackupAnswers Backup,
    string LicenseFilePath,
    AdministratorAnswers Administrator,
    PrerequisitesAnswers Prerequisites);

public sealed record SiteAnswers(
    string HostName,
    int HttpsPort,
    bool HttpRedirect,
    string? CertificateThumbprint,
    bool AllowSelfSignedCertificate);

public enum SqlAuthentication
{
    Windows,
    Sql,
}

public enum SqlEncrypt
{
    Mandatory,
    Strict,
    Optional,
}

public sealed record SqlAnswers(
    string Server,
    string DatabaseName,
    SqlAuthentication OperatorAuthentication,
    string? OperatorSqlLogin,
    SqlAuthentication AppAuthentication,
    string? AppWindowsAccount,
    string? DataFilePath,
    string? LogFilePath,
    int InitialDataSizeMb,
    int InitialLogSizeMb,
    SqlEncrypt Encrypt,
    bool TrustServerCertificate);

public sealed record BackupAnswers(
    string BackupRoot,
    TimeOnly FullBackupTime,
    int LogBackupEveryMinutes,
    int RetentionDays,
    DayOfWeek IntegrityCheckDay,
    TimeOnly IntegrityCheckTime);

public sealed record AdministratorAnswers(string UserName, string DisplayName, string? Email);

public sealed record PrerequisitesAnswers(bool InstallIisFeatures, bool InstallHostingBundle);

public enum IssueSeverity
{
    Error,
    Warning,
}

/// <summary>O problema pe un camp al fisierului de raspunsuri; mesajul e in romana.</summary>
public sealed record AnswersIssue(string Field, string Message, IssueSeverity Severity = IssueSeverity.Error);
