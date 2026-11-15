using System.Reflection;

namespace Erp.Hosting;

/// <summary>Versiunea produsului si ultima migrare, incorporate la build (AssemblyMetadata, 10.5).</summary>
public sealed record ErpBuildInfo(string Version, string? LastMigration)
{
    public static ErpBuildInfo From(Assembly assembly)
    {
        string? Metadata(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.Ordinal))?.Value;

        return new ErpBuildInfo(Metadata("ErpVersion") ?? "0.0.0", Metadata("ErpLastMigration"));
    }
}
