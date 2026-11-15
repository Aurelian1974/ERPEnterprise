using System.Text;
using System.Text.RegularExpressions;
using DbUp.Engine;

namespace Erp.Setup.Sql.Database;

/// <summary>
/// Scripturile bazei din kit (database/), in ordinea de executie (8.5, 8.6). Numele din jurnal este „&lt;modul&gt;/&lt;fisier&gt;”.
/// Ordinea se transmite DbUp prin RunGroupOrder, pentru ca DbUp sorteaza altfel scripturile dupa nume.
/// </summary>
public sealed partial class KitScripts
{
    /// <summary>Subfolderele din Programmability, in ordinea de executie.</summary>
    public static readonly IReadOnlyList<string> ProgrammabilityFolders = ["Functions", "Views", "Procedures"];

    private KitScripts(IReadOnlyList<SqlScript> migrations, IReadOnlyList<SqlScript> programmability, IReadOnlyList<SqlScript> seed)
    {
        Migrations = migrations;
        Programmability = programmability;
        Seed = seed;
    }

    public IReadOnlyList<SqlScript> Migrations { get; }

    public IReadOnlyList<SqlScript> Programmability { get; }

    public IReadOnlyList<SqlScript> Seed { get; }

    /// <summary>Ultima migrare din kit (manifest.lastMigration, 10.5); null daca nu exista migrari.</summary>
    public string? LastMigration => Migrations.Count == 0 ? null : Migrations[^1].Name;

    /// <param name="databaseRoot">Folderul database/ din kit.</param>
    public static KitScripts Load(string databaseRoot)
    {
        var errors = new List<string>();

        var migrations = new List<(string Prefix, int Module, string Name, string Path)>();
        foreach (var (schema, moduleIndex, folder) in ModuleFolders(Path.Combine(databaseRoot, "migrations"), errors))
        {
            var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var fileName = Path.GetFileName(file);
                if (Path.GetDirectoryName(file) != folder || !MigrationName().IsMatch(fileName))
                {
                    errors.Add($"migrations/{schema}/{Path.GetRelativePath(folder, file).Replace('\\', '/')}: numele nu respectă MIG-01 (AAAA.LL.NNN_Descriere.sql, direct în folderul modulului).");
                    continue;
                }

                var prefix = fileName[..11];
                if (!prefixes.TryAdd(prefix, fileName))
                {
                    errors.Add($"migrations/{schema}: prefixul {prefix} este folosit de {prefixes[prefix]} și de {fileName}.");
                    continue;
                }

                migrations.Add((prefix, moduleIndex, $"{schema}/{fileName}", file));
            }
        }

        var programmability = new List<(int Module, int Folder, string File, string Name, string Path)>();
        foreach (var (schema, moduleIndex, folder) in ModuleFolders(Path.Combine(databaseRoot, "programmability"), errors))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                var parts = relative.Split('/');
                var folderIndex = parts.Length == 2 ? IndexOf(ProgrammabilityFolders, parts[0]) : -1;
                if (folderIndex < 0 || !parts[1].EndsWith(".sql", StringComparison.Ordinal))
                {
                    errors.Add($"programmability/{schema}/{relative}: obiectele stau în {string.Join(", ", ProgrammabilityFolders)} (fișiere .sql).");
                    continue;
                }

                programmability.Add((moduleIndex, folderIndex, parts[1], $"{schema}/{relative}", file));
            }
        }

        var seed = new List<(int Module, string File, string Name, string Path)>();
        foreach (var (schema, moduleIndex, folder) in ModuleFolders(Path.Combine(databaseRoot, "seed"), errors))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (relative.Contains('/') || !relative.EndsWith(".sql", StringComparison.Ordinal))
                {
                    errors.Add($"seed/{schema}/{relative}: datele livrate sunt fișiere .sql direct în folderul modulului.");
                    continue;
                }

                seed.Add((moduleIndex, relative, $"{schema}/{relative}", file));
            }
        }

        if (errors.Count > 0)
        {
            throw new SqlSetupException("Scripturile bazei din kit sunt invalide: " + string.Join(" ", errors));
        }

        return new KitScripts(
            ToScripts(migrations.OrderBy(m => m.Prefix, StringComparer.Ordinal).ThenBy(m => m.Module).Select(m => (m.Name, m.Path))),
            ToScripts(programmability.OrderBy(p => p.Module).ThenBy(p => p.Folder).ThenBy(p => p.File, StringComparer.Ordinal).Select(p => (p.Name, p.Path))),
            ToScripts(seed.OrderBy(s => s.Module).ThenBy(s => s.File, StringComparer.Ordinal).Select(s => (s.Name, s.Path))));
    }

    private static IEnumerable<(string Schema, int Index, string Folder)> ModuleFolders(string root, List<string> errors)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var folder in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            var schema = Path.GetFileName(folder);
            var index = IndexOf(ModuleOrder.Schemas, schema);
            if (index < 0)
            {
                errors.Add($"{Path.GetFileName(root)}/{schema}: modul necunoscut.");
                continue;
            }

            yield return (schema, index, folder);
        }

        foreach (var file in Directory.EnumerateFiles(root))
        {
            errors.Add($"{Path.GetFileName(root)}/{Path.GetFileName(file)}: scripturile stau în folderul modulului.");
        }
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<SqlScript> ToScripts(IEnumerable<(string Name, string Path)> ordered)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        return ordered
            .Select((item, index) => new SqlScript(item.Name, File.ReadAllText(item.Path, encoding), new SqlScriptOptions { RunGroupOrder = index }))
            .ToList();
    }

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{3}_[A-Za-z0-9_]+\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationName();
}
