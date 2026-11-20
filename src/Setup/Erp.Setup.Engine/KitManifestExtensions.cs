using Erp.Setup.Core.Kit;
using Erp.Setup.Sql.Database;

namespace Erp.Setup.Engine;

/// <summary>Leaga manifestul kitului (Erp.Setup.Core) de jurnalul de versiuni din Erp.Setup.Sql (08, 5.9).</summary>
internal static class KitManifestExtensions
{
    /// <summary>Versiunile scripturilor din manifest, cu versiunea kitului ca versiune a produsului.</summary>
    public static ScriptVersions ScriptVersionsOf(this KitManifest manifest) =>
        new(manifest.ScriptVersions ?? new Dictionary<string, int>(StringComparer.Ordinal), manifest.Version);
}
