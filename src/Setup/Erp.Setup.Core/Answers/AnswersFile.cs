namespace Erp.Setup.Core.Answers;

// Modelul brut al fisierului de raspunsuri (6.2): toate campurile optionale la citire;
// obligativitatea si valorile implicite se aplica in AnswersValidator.
public sealed class AnswersFile
{
    public string? Instance { get; set; }

    public string? InstallRoot { get; set; }

    public string? DataRoot { get; set; }

    public SiteSection? Site { get; set; }

    public SqlSection? Sql { get; set; }

    public BackupSection? Backup { get; set; }

    public LicenseSection? License { get; set; }

    public AdministratorSection? Administrator { get; set; }

    public PrerequisitesSection? Prerequisites { get; set; }

    public sealed class SiteSection
    {
        public string? HostName { get; set; }

        public int? HttpsPort { get; set; }

        public bool? HttpRedirect { get; set; }

        public string? CertificateThumbprint { get; set; }

        public bool? AllowSelfSignedCertificate { get; set; }
    }

    public sealed class SqlSection
    {
        public string? Server { get; set; }

        public string? DatabaseName { get; set; }

        public string? OperatorAuthentication { get; set; }

        public string? OperatorSqlLogin { get; set; }

        public string? AppAuthentication { get; set; }

        public string? AppWindowsAccount { get; set; }

        public string? DataFilePath { get; set; }

        public string? LogFilePath { get; set; }

        public int? InitialDataSizeMb { get; set; }

        public int? InitialLogSizeMb { get; set; }

        public string? Encrypt { get; set; }

        public bool? TrustServerCertificate { get; set; }
    }

    public sealed class BackupSection
    {
        public string? BackupRoot { get; set; }

        public string? FullBackupTime { get; set; }

        public int? LogBackupEveryMinutes { get; set; }

        public int? RetentionDays { get; set; }

        public string? IntegrityCheckDay { get; set; }

        public string? IntegrityCheckTime { get; set; }
    }

    public sealed class LicenseSection
    {
        public string? FilePath { get; set; }
    }

    public sealed class AdministratorSection
    {
        public string? UserName { get; set; }

        public string? DisplayName { get; set; }

        public string? Email { get; set; }
    }

    public sealed class PrerequisitesSection
    {
        public bool? InstallIisFeatures { get; set; }

        public bool? InstallHostingBundle { get; set; }
    }
}
