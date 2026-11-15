using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erp.Setup.Core.Kit;

/// <summary>manifest.json din kit (4.2).</summary>
public sealed record KitManifest
{
    public required string Product { get; init; }

    public required string Version { get; init; }

    public required string ReleaseDate { get; init; }

    public required string MinUpgradeFrom { get; init; }

    public required int MinSqlServerMajorVersion { get; init; }

    public required int SqlCompatibilityLevel { get; init; }

    public required string DatabaseCollation { get; init; }

    public required string HostingBundleVersion { get; init; }

    public string? LastMigration { get; init; }

    public required IReadOnlyList<KitFile> Files { get; init; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static KitManifest Parse(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<KitManifest>(utf8Json, JsonOptions)
        ?? throw new JsonException("Manifestul este gol.");
}

public sealed record KitFile(string Path, string Sha256, long Size);
