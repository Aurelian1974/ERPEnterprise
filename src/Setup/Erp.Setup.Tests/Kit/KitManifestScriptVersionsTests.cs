using System.Text;
using Erp.Setup.Core.Kit;

namespace Erp.Setup.Tests.Kit;

/// <summary>
/// Contractul dintre build/KitBuilder.psm1 (New-KitManifest) si installer pentru versiunile scripturilor (08, 5.9 si 7):
/// manifestul scris de PowerShell se citeste in C# cu aceleasi chei ca jurnalul DbUp.
/// </summary>
public sealed class KitManifestScriptVersionsTests
{
    private const string Head = """
        {
          "product": "ERP",
          "version": "2027.1.0",
          "releaseDate": "2027-01-20",
          "minUpgradeFrom": "2026.2.0",
          "minSqlServerMajorVersion": 16,
          "sqlCompatibilityLevel": 160,
          "databaseCollation": "Latin1_General_100_CS_AS_SC_UTF8",
          "hostingBundleVersion": "10.0.3",
          "lastMigration": "core/2026.11.001_Create_Company.sql",
          "kitKeyId": 1,
        """;

    [Fact]
    public void Script_versions_written_by_the_kit_builder_are_read_with_the_journal_names()
    {
        var manifest = Parse("""
              "scriptVersions": {
                "core/2026.11.001_Create_Company.sql": 1,
                "core/Procedures/core.usp_Company_Save.sql": 7,
                "core/01_Roles.sql": 3
              },
              "files": []
            }
            """);

        Assert.NotNull(manifest.ScriptVersions);
        Assert.Equal(7, manifest.ScriptVersions["core/Procedures/core.usp_Company_Save.sql"]);
        Assert.Equal(1, manifest.ScriptVersions["core/2026.11.001_Create_Company.sql"]);
        Assert.Equal(3, manifest.ScriptVersions["core/01_Roles.sql"]);
    }

    [Theory]
    [InlineData("\"scriptVersions\": null, \"files\": [] }")]
    [InlineData("\"files\": [] }")]
    public void A_kit_without_versions_is_still_valid(string tail)
    {
        Assert.Null(Parse(tail).ScriptVersions);
    }

    private static KitManifest Parse(string tail) => KitManifest.Parse(Encoding.UTF8.GetBytes(Head + tail));
}
