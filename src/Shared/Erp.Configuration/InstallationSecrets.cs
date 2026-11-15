using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Erp.Configuration;

/// <summary>
/// Secretele aplicatiei (8.10, NET-17, SEC-02): secrets.app.bin = ProtectedData.Protect(utf8Json, entropie, LocalMachine),
/// cu entropia UTF8("Erp.Secrets.v1|" + Instanta). Continutul JSON: perechi cheie de configurare - valoare
/// (de exemplu „Database:Password”). Folosit identic de installer (scriere) si de Erp.Host / Erp.Worker (citire).
/// </summary>
[SupportedOSPlatform("windows")]
public static class InstallationSecrets
{
    public const string FileName = "secrets.app.bin";
    public const string DatabasePasswordKey = "Database:Password";

    public static byte[] Protect(IReadOnlyDictionary<string, string> secrets, string instance) =>
        ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(secrets), Entropy(instance), DataProtectionScope.LocalMachine);

    public static IReadOnlyDictionary<string, string> Unprotect(byte[] protectedData, string instance)
    {
        var json = ProtectedData.Unprotect(protectedData, Entropy(instance), DataProtectionScope.LocalMachine);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    /// <summary>Scriere atomica; ACL-ul se mosteneste din folderul config\ (citire: pool-ul Host si serviciul, 3.2).</summary>
    public static void Write(string path, IReadOnlyDictionary<string, string> secrets, string instance)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, Protect(secrets, instance));
        File.Move(temporary, path, overwrite: true);
    }

    public static IReadOnlyDictionary<string, string> Read(string path, string instance) => Unprotect(File.ReadAllBytes(path), instance);

    private static byte[] Entropy(string instance) => Encoding.UTF8.GetBytes("Erp.Secrets.v1|" + instance);
}

/// <summary>Caile instantei in DataRoot (3.2); componentele le gasesc prin ERP_INSTANCE_DIR (8.10).</summary>
public sealed record InstanceDataPaths(string InstanceDirectory)
{
    public const string InstanceDirectoryVariable = "ERP_INSTANCE_DIR";

    public string InstanceJson => Path.Combine(InstanceDirectory, "instance.json");

    public string ConfigDirectory => Path.Combine(InstanceDirectory, "config");

    public string AppSettings => Path.Combine(ConfigDirectory, "appsettings.Installation.json");

    public string Secrets => Path.Combine(ConfigDirectory, "secrets.app.bin");

    public string LicenseDirectory => Path.Combine(InstanceDirectory, "license");

    public string License => Path.Combine(LicenseDirectory, "erp.lic");

    public string KeysDirectory => Path.Combine(InstanceDirectory, "keys");

    public string LogsDirectory => Path.Combine(InstanceDirectory, "logs");

    public string HostLogs => Path.Combine(LogsDirectory, "host");

    public string WorkerLogs => Path.Combine(LogsDirectory, "worker");

    public string SetupLogs => Path.Combine(LogsDirectory, "setup");

    public string MaintenanceDirectory => Path.Combine(InstanceDirectory, "maintenance");

    public string MaintenanceFlag => Path.Combine(MaintenanceDirectory, "maintenance.flag");

    public string UpgradeDirectory => Path.Combine(InstanceDirectory, "upgrade");

    public string InstallDirectory => Path.Combine(InstanceDirectory, "install");

    public static InstanceDataPaths For(string dataRoot, string instance) => new(Path.Combine(dataRoot, instance));
}
