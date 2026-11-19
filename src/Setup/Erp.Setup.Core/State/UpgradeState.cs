using System.Text.Json;

namespace Erp.Setup.Core.State;

/// <summary>
/// upgrade\upgrade-state.json (08, 10.2, U-02): de unde pleaca actualizarea si ce s-a facut, ca revenirea (automata sau
/// „Revino la versiunea anterioară” dupa o inchidere fortata) sa stie ce are de readus. Nu contine secrete.
/// </summary>
public sealed record UpgradeState
{
    public const string FileName = "upgrade-state.json";

    public int FormatVersion { get; init; } = 1;

    public required string FromVersion { get; init; }

    public required string ToVersion { get; init; }

    public string? FromLastMigration { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    /// <summary>Backup-ul COPY_ONLY de la U-04, pe serverul SQL.</summary>
    public string? BackupFile { get; init; }

    public bool SnapshotCreated { get; init; }

    /// <summary>Copia folderului config\ (U-04), in upgrade\.</summary>
    public string? ConfigBackupDirectory { get; init; }

    /// <summary>U-06 a inceput: revenirea readuce si baza (R-02).</summary>
    public bool DatabaseChanged { get; init; }

    public IReadOnlyList<string> CompletedSteps { get; init; } = [];

    public static string PathFor(string dataRoot, string instance) => Path.Combine(dataRoot, instance, "upgrade", FileName);

    public static UpgradeState? Read(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<UpgradeState>(File.ReadAllBytes(path), InstanceStateStore.JsonOptions) : null;

    public static void Write(string path, UpgradeState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(state, InstanceStateStore.JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }
}
