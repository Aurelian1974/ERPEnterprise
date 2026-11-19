namespace Erp.Configuration;

/// <summary>Caile unei instalari in C:\ProgramData\Erp\&lt;Instanta&gt; (08, 5.7); componentele le gasesc prin ERP_INSTANCE_DIR.</summary>
public sealed record InstanceDataPaths(string InstanceDirectory)
{
    public const string InstanceDirectoryVariable = "ERP_INSTANCE_DIR";

    public string InstanceJson => Path.Combine(InstanceDirectory, "instance.json");

    public string ConfigDirectory => Path.Combine(InstanceDirectory, "config");

    public string AppSettings => Path.Combine(ConfigDirectory, "appsettings.Installation.json");

    public string LogsDirectory => Path.Combine(InstanceDirectory, "logs");

    public string HostLogs => Path.Combine(LogsDirectory, "host");

    public string WorkerLogs => Path.Combine(LogsDirectory, "worker");

    public string SetupLogs => Path.Combine(LogsDirectory, "setup");

    public string MaintenanceDirectory => Path.Combine(InstanceDirectory, "maintenance");

    public string MaintenanceFlag => Path.Combine(MaintenanceDirectory, "maintenance.flag");

    public string MaintenancePage => Path.Combine(MaintenanceDirectory, "maintenance.html");

    public string AppOfflineTemplate => Path.Combine(MaintenanceDirectory, "app_offline.template.htm");

    public string UpgradeDirectory => Path.Combine(InstanceDirectory, "upgrade");

    public string InstallDirectory => Path.Combine(InstanceDirectory, "install");

    /// <summary>Fisierul de raspunsuri scris de expert la finalul instalarii (08, 6.2).</summary>
    public string Answers => Path.Combine(InstallDirectory, "answers.json");

    /// <summary>Parola administratorului, doar la instalarea neasistata (08, 5.10, D29).</summary>
    public string InitialAdmin => Path.Combine(InstallDirectory, "initial-admin.txt");

    /// <summary>Partea publica a certificatului HTTPS intern, de distribuit pe calculatoarele utilizatorilor (08, 5.11).</summary>
    public string InternalCertificate => Path.Combine(InstanceDirectory, "certificat-erp.cer");

    public static InstanceDataPaths For(string dataRoot, string instance) => new(Path.Combine(dataRoot, instance));
}
