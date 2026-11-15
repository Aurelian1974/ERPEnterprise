using System.Text;

namespace Erp.Testing.Sql;

/// <summary>Un folder database/ ca in kit, in folderul temporar.</summary>
public sealed class TempKitDatabase : IDisposable
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
