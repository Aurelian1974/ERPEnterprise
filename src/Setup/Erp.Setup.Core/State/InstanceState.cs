using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erp.Setup.Core.State;

public enum InstanceLifecycle
{
    Installing,
    Installed,
    Upgrading,
    RollingBack,
    Maintenance,
    Failed,
}

/// <summary>Starea instalarii (6.5). Nu contine secrete.</summary>
public sealed record InstanceState
{
    public required string Instance { get; init; }

    public string Product { get; init; } = "ERP";

    public Guid InstallId { get; init; }

    public string? CurrentVersion { get; init; }

    public IReadOnlyList<string> PreviousVersions { get; init; } = [];

    public required InstanceLifecycle State { get; init; }

    public DateTimeOffset? InstalledAt { get; init; }

    public DateTimeOffset? LastUpgradeAt { get; init; }

    public string? LastMigration { get; init; }

    public required string InstallRoot { get; init; }

    public required string DataRoot { get; init; }

    public string? SiteName { get; init; }

    public string? HostName { get; init; }

    public int? HttpsPort { get; init; }

    public string? ServiceName { get; init; }

    public string? SqlServer { get; init; }

    public string? DatabaseName { get; init; }

    public string? AppAuthentication { get; init; }

    public string? AppLogin { get; init; }

    public string? MigratorLogin { get; init; }

    public string? BackupRoot { get; init; }

    public IReadOnlyList<string> CompletedSteps { get; init; } = [];
}

/// <summary>Citirea si scrierea atomica a <c>DataRoot\&lt;Instanta&gt;\instance.json</c>.</summary>
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
}
