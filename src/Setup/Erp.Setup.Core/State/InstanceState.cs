using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Erp.Setup.Core.Answers;

namespace Erp.Setup.Core.State;

/// <summary>Starea instalarii (08, 5.7).</summary>
public enum InstanceLifecycle
{
    Installing,
    Installed,
    Upgrading,
    RollingBack,
    Maintenance,
}

public enum BackupEdition
{
    /// <summary>Joburi SQL Agent (08, 5.13).</summary>
    Agent,

    /// <summary>SQL Express: fara backup automat.</summary>
    None,
}

/// <summary>instance.json, formatul 2 (08, 5.7). Nu contine secrete.</summary>
public sealed record InstanceState
{
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required string Instance { get; init; }

    public string? CurrentVersion { get; init; }

    public IReadOnlyList<string> PreviousVersions { get; init; } = [];

    public required InstanceLifecycle State { get; init; }

    public Guid InstallId { get; init; }

    public DateTimeOffset? InstalledAt { get; init; }

    public DateTimeOffset? LastUpgradeAt { get; init; }

    public required string InstallRoot { get; init; }

    public string? SiteName { get; init; }

    public string? HostName { get; init; }

    public int? HttpsPort { get; init; }

    public bool HttpRedirect { get; init; }

    public CertificateKind CertificateKind { get; init; } = CertificateKind.Internal;

    public string? CertificateThumbprint { get; init; }

    public string? ServiceName { get; init; }

    public string? SqlServer { get; init; }

    public string? DatabaseName { get; init; }

    public bool SqlIsRemote { get; init; }

    public bool TrustServerCertificate { get; init; }

    public AppIdentity AppIdentity { get; init; } = AppIdentity.VirtualAccounts;

    /// <summary>Contul gMSA sau al calculatorului (DOMENIU\nume$); null pentru conturile virtuale.</summary>
    public string? AppIdentityAccount { get; init; }

    public IReadOnlyList<string> AppSqlLogins { get; init; } = [];

    public string? DataProtectionCertificateThumbprint { get; init; }

    public DateTimeOffset? RecoveryKeyExportedAt { get; init; }

    public string? BackupFolder { get; init; }

    public BackupEdition BackupEdition { get; init; } = BackupEdition.Agent;

    public string? FullBackupTime { get; init; }

    public int LogBackupEveryMinutes { get; init; } = 15;

    public int RetentionDays { get; init; } = 14;

    public DayOfWeek IntegrityCheckDay { get; init; } = DayOfWeek.Saturday;

    public string? IntegrityCheckTime { get; init; }

    public string? LastMigration { get; init; }

    /// <summary>Backup-urile COPY_ONLY ale actualizarilor (U-04), cele mai noi primele; se pastreaza ultimele 3 (U-10).</summary>
    public IReadOnlyList<string> UpgradeBackups { get; init; } = [];

    public IReadOnlyList<string> CompletedSteps { get; init; } = [];
}

/// <summary>Citirea si scrierea atomica a <c>C:\ProgramData\Erp\&lt;Instanta&gt;\instance.json</c> (08, 5.7).</summary>
public static class InstanceStateStore
{
    public const string FileName = "instance.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string PathFor(string dataRoot, string instance) => Path.Combine(dataRoot, instance, FileName);

    public static InstanceState? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<InstanceState>(File.ReadAllBytes(path), JsonOptions)
            ?? throw new JsonException($"{FileName} este gol.");
    }

    public static void Write(string path, InstanceState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Instalarile de pe calculator (ecranul 0, 08, 5.3): fiecare folder din DataRoot cu instance.json lizibil.</summary>
    public static IReadOnlyList<InstanceState> FindAll(string dataRoot)
    {
        if (!Directory.Exists(dataRoot))
        {
            return [];
        }

        var found = new List<InstanceState>();
        foreach (var directory in Directory.EnumerateDirectories(dataRoot))
        {
            try
            {
                if (Read(Path.Combine(directory, FileName)) is { } state)
                {
                    found.Add(state);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Un instance.json ilizibil nu ascunde celelalte instalari.
            }
        }

        return [.. found.OrderBy(s => s.Instance, StringComparer.Ordinal)];
    }
}
