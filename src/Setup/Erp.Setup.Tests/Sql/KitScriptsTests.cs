using System.Text;
using System.Text.RegularExpressions;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Database;

namespace Erp.Setup.Tests.Sql;

/// <summary>Un folder database/ ca in kit, in folderul temporar.</summary>
internal sealed class TempKitDatabase : IDisposable
{
    public TempKitDatabase()
    {
        Directory.CreateDirectory(Root);
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "erp-kitdb-" + Guid.NewGuid().ToString("N"));

    public TempKitDatabase Add(string relativePath, string content)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return this;
    }

    /// <summary>Scripturile reale din depozit, asezate ca in kit (maparea din package-kit.ps1 pentru ids si hangfire).</summary>
    public static TempKitDatabase FromRepository()
    {
        var kit = new TempKitDatabase();
        var repo = TestPaths.RepositoryRoot();
        var sources = Modules.Select(m => ($"src/Modules/{m.Module}/Erp.{m.Module}.Infrastructure/Database", m.Schema))
            .Append(("src/Auth/Erp.Auth/Database", "ids"))
            .Append(("src/Worker/Erp.Worker/Database", "hangfire"));
        foreach (var (source, schema) in sources)
        {
            foreach (var kind in new[] { "Migrations", "Programmability", "Seed" })
            {
                var folder = Path.Combine(repo, source, kind);
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(folder, "*.sql", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                    kit.Add($"{kind.ToLowerInvariant()}/{schema}/{relative}", File.ReadAllText(file));
                }
            }
        }

        return kit;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    // Aceeasi mapare ca $script:ModuleSchemas din build/KitBuilder.psm1 (verificata de Module_map_matches_the_kit_builder).
    public static readonly (string Module, string Schema)[] Modules =
    [
        ("Core", "core"), ("Finance", "fin"), ("Sales", "sales"), ("Purchasing", "purch"), ("Inventory", "inv"), ("Integrations", "intg"),
        ("Wms", "wms"), ("Tms", "tms"), ("Manufacturing", "mfg"), ("Hr", "hr"), ("Pos", "pos"), ("Construction", "cons"), ("Crm", "crm"), ("Reporting", "rpt"),
    ];
}

public sealed class KitScriptsTests
{
    [Fact]
    public void Migrations_are_ordered_by_prefix_then_module_order()
    {
        using var kit = new TempKitDatabase()
            .Add("migrations/sales/2026.11.001_Sales.sql", "SELECT 1;")
            .Add("migrations/core/2026.12.001_Later.sql", "SELECT 1;")
            .Add("migrations/hangfire/2026.11.001_Hangfire.sql", "SELECT 1;")
            .Add("migrations/core/2026.11.001_Core.sql", "SELECT 1;")
            .Add("migrations/ids/2026.11.002_Ids.sql", "SELECT 1;");

        var scripts = KitScripts.Load(kit.Root);

        Assert.Equal(
            ["core/2026.11.001_Core.sql", "hangfire/2026.11.001_Hangfire.sql", "sales/2026.11.001_Sales.sql", "ids/2026.11.002_Ids.sql", "core/2026.12.001_Later.sql"],
            scripts.Migrations.Select(s => s.Name));
        Assert.Equal([0, 1, 2, 3, 4], scripts.Migrations.Select(s => s.SqlScriptOptions.RunGroupOrder));
        Assert.Equal("core/2026.12.001_Later.sql", scripts.LastMigration);
    }

    [Fact]
    public void Programmability_is_ordered_by_module_then_functions_views_procedures_then_name()
    {
        using var kit = new TempKitDatabase()
            .Add("programmability/sales/Procedures/sales.usp_A.sql", "CREATE OR ALTER PROCEDURE sales.usp_A AS SELECT 1;")
            .Add("programmability/core/Procedures/core.usp_B.sql", "x")
            .Add("programmability/core/Views/02_core.v_Second.sql", "x")
            .Add("programmability/core/Views/01_core.v_First.sql", "x")
            .Add("programmability/core/Functions/core.fn_X.sql", "x");

        var names = KitScripts.Load(kit.Root).Programmability.Select(s => s.Name);

        Assert.Equal(
            ["core/Functions/core.fn_X.sql", "core/Views/01_core.v_First.sql", "core/Views/02_core.v_Second.sql", "core/Procedures/core.usp_B.sql", "sales/Procedures/sales.usp_A.sql"],
            names);
    }

    [Fact]
    public void Seed_is_ordered_by_module_then_name()
    {
        using var kit = new TempKitDatabase()
            .Add("seed/fin/01_Accounts.sql", "x")
            .Add("seed/core/02_VatRates.sql", "x")
            .Add("seed/core/01_Currencies.sql", "x");

        Assert.Equal(["core/01_Currencies.sql", "core/02_VatRates.sql", "fin/01_Accounts.sql"], KitScripts.Load(kit.Root).Seed.Select(s => s.Name));
    }

    [Theory]
    [InlineData("migrations/core/2026.11.1_Bad.sql", "MIG-01")]
    [InlineData("migrations/core/sub/2026.11.001_Nested.sql", "MIG-01")]
    [InlineData("migrations/Core/2026.11.001_Case.sql", "modul necunoscut")]
    [InlineData("migrations/2026.11.001_Root.sql", "folderul modulului")]
    [InlineData("programmability/core/core.usp_Root.sql", "Functions, Views, Procedures")]
    [InlineData("programmability/core/Types/core.tt_X.sql", "Functions, Views, Procedures")]
    [InlineData("seed/core/sub/01_X.sql", "direct în folderul modulului")]
    public void Invalid_layout_is_refused(string path, string expected)
    {
        using var kit = new TempKitDatabase().Add(path, "SELECT 1;");

        var ex = Assert.Throws<SqlSetupException>(() => KitScripts.Load(kit.Root));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Duplicate_prefix_in_the_same_module_is_refused()
    {
        using var kit = new TempKitDatabase()
            .Add("migrations/core/2026.11.001_A.sql", "SELECT 1;")
            .Add("migrations/core/2026.11.001_B.sql", "SELECT 1;");

        Assert.Contains("prefixul 2026.11.001", Assert.Throws<SqlSetupException>(() => KitScripts.Load(kit.Root)).Message);
    }

    [Fact]
    public void Missing_folders_mean_no_scripts()
    {
        using var kit = new TempKitDatabase();

        var scripts = KitScripts.Load(kit.Root);

        Assert.Empty(scripts.Migrations);
        Assert.Null(scripts.LastMigration);
    }

    [Fact]
    public void Repository_scripts_load_and_hangfire_runs_after_ids()
    {
        using var kit = TempKitDatabase.FromRepository();

        var names = KitScripts.Load(kit.Root).Migrations.Select(s => s.Name).ToList();

        Assert.Equal(
            ["core/2026.11.001_Create_InstallationLicense.sql", "ids/2026.11.001_Create_UserAccount.sql", "hangfire/2026.11.001_Hangfire_1_8_25_Install.sql"],
            names.Take(3));
    }

    [Fact]
    public void Module_map_matches_the_kit_builder()
    {
        var builder = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot(), "build", "KitBuilder.psm1"));
        var block = Regex.Match(builder, @"\$script:ModuleSchemas = \[ordered\]@\{(?<map>[^}]*)\}").Groups["map"].Value;
        var map = Regex.Matches(block, @"(?<module>[A-Z][A-Za-z]+) = '(?<schema>[a-z]+)'")
            .Select(m => (m.Groups["module"].Value, m.Groups["schema"].Value));

        Assert.Equal(TempKitDatabase.Modules, map);
    }

    [Fact]
    public void Module_order_matches_the_kit_builder()
    {
        var builder = File.ReadAllText(Path.Combine(TestPaths.RepositoryRoot(), "build", "KitBuilder.psm1"));
        var list = Regex.Match(builder, @"\$script:ModuleOrder = @\((?<list>[^)]*)\)").Groups["list"].Value;

        Assert.Equal(ModuleOrder.Schemas, Regex.Matches(list, "'([a-z]+)'").Select(m => m.Groups[1].Value));
    }

    [Fact]
    public void Hangfire_migration_matches_the_pinned_package_version()
    {
        var repo = TestPaths.RepositoryRoot();
        var props = File.ReadAllText(Path.Combine(repo, "Directory.Packages.props"));
        var version = Regex.Match(props, "Include=\"Hangfire.SqlServer\" Version=\"(?<v>[^\"]+)\"").Groups["v"].Value;
        var migrations = Directory.GetFiles(Path.Combine(repo, "src", "Worker", "Erp.Worker", "Database", "Migrations"), "*.sql")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.False(string.IsNullOrEmpty(version));
        Assert.EndsWith($"_Hangfire_{version.Replace('.', '_')}_Install.sql", migrations[^1], StringComparison.Ordinal);
    }
}
