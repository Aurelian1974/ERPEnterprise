using Erp.Setup.Sql.Database;

namespace Erp.DevDatabase.Sync;

/// <summary>Un script SQL din depozit: tipul, numele din jurnal (ca in kit) si fisierul sursa.</summary>
public sealed record RepositoryScript(ScriptKind Kind, string JournalName, string KitPath, string SourcePath);

/// <summary>
/// Scripturile SQL ale depozitului asezate ca in kit (08, 5.9 si 7): aceeasi mapare modul → schema ca
/// build/KitBuilder.psm1 ($script:ModuleSchemas), verificata de testul Module_map_matches_the_kit_builder.
/// </summary>
public static class RepositoryScripts
{
    public static readonly (string Module, string Schema)[] Modules =
    [
        ("Core", "core"), ("Finance", "fin"), ("Sales", "sales"), ("Purchasing", "purch"), ("Inventory", "inv"), ("Integrations", "intg"),
        ("Wms", "wms"), ("Tms", "tms"), ("Manufacturing", "mfg"), ("Hr", "hr"), ("Pos", "pos"), ("Construction", "cons"), ("Crm", "crm"), ("Reporting", "rpt"),
    ];

    private static readonly (string Folder, string KitFolder, ScriptKind Kind)[] Kinds =
    [
        ("Migrations", "migrations", ScriptKind.Migration),
        ("Programmability", "programmability", ScriptKind.Programmability),
        ("Seed", "seed", ScriptKind.Seed),
    ];

    /// <summary>Radacina depozitului: primul folder parinte care contine Erp.slnx.</summary>
    public static string FindRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Erp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"Depozitul ERP (Erp.slnx) nu a fost găsit pornind din {start}.");
    }

    public static string BootstrapDirectory(string repoRoot) => Path.Combine(repoRoot, "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap");

    /// <summary>Folderele Database/ existente, cu schema lor: modulele, Erp.Auth (ids) si Erp.Worker (hangfire).</summary>
    public static IReadOnlyList<(string Schema, string Folder)> DatabaseFolders(string repoRoot) =>
        Modules.Select(m => (m.Schema, Path.Combine(repoRoot, "src", "Modules", m.Module, $"Erp.{m.Module}.Infrastructure", "Database")))
            .Append(("ids", Path.Combine(repoRoot, "src", "Auth", "Erp.Auth", "Database")))
            .Append(("hangfire", Path.Combine(repoRoot, "src", "Worker", "Erp.Worker", "Database")))
            .Where(f => Directory.Exists(f.Item2))
            .ToArray();

    /// <summary>Toate scripturile, cu numele din jurnal construit ca in KitScripts.</summary>
    public static IReadOnlyList<RepositoryScript> Enumerate(string repoRoot)
    {
        var scripts = new List<RepositoryScript>();
        foreach (var (schema, database) in DatabaseFolders(repoRoot))
        {
            foreach (var (folder, kitFolder, kind) in Kinds)
            {
                var source = Path.Combine(database, folder);
                if (!Directory.Exists(source))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(source, "*.sql", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                {
                    var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                    scripts.Add(new RepositoryScript(kind, $"{schema}/{relative}", $"{kitFolder}/{schema}/{relative}", file));
                }
            }
        }

        return scripts;
    }

    /// <summary>Copiaza scripturile in folderul database/ al unui kit (migrations/, programmability/, seed/).</summary>
    public static void LayOut(string repoRoot, string kitDatabaseRoot)
    {
        foreach (var script in Enumerate(repoRoot))
        {
            var target = Path.Combine(kitDatabaseRoot, script.KitPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(script.SourcePath, target, overwrite: true);
        }
    }
}
