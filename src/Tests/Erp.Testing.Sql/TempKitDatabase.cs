using System.Text;
using Erp.DevDatabase.Sync;

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

    /// <summary>Scripturile reale din depozit, asezate ca in kit (aceeasi asezare ca baza de dezvoltare, RepositoryScripts).</summary>
    public static TempKitDatabase FromRepository()
    {
        var kit = new TempKitDatabase();
        RepositoryScripts.LayOut(TestPaths.RepositoryRoot(), kit.Root);
        return kit;
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);

    // Aceeasi mapare ca $script:ModuleSchemas din build/KitBuilder.psm1 (verificata de Module_map_matches_the_kit_builder).
    public static readonly (string Module, string Schema)[] Modules = RepositoryScripts.Modules;
}
