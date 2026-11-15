using System.Text;
using System.Text.RegularExpressions;

namespace Erp.Setup.Sql.Database;

/// <summary>Pasul I-07 (8.3): ruleaza scripturile din database/bootstrap/ in baza ERP, in ordinea numelui, lot cu lot.</summary>
public sealed partial class BootstrapRunner(SqlOperator sql)
{
    public async Task<int> RunAsync(string databaseName, string bootstrapDirectory, CancellationToken ct)
    {
        var files = Directory.EnumerateFiles(bootstrapDirectory, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
        {
            throw new SqlSetupException($"Folderul {bootstrapDirectory} nu conține scripturi de bootstrap.");
        }

        await using var connection = await sql.OpenAsync(databaseName, ct).ConfigureAwait(false);
        foreach (var file in files)
        {
            var batches = SplitBatches(await File.ReadAllTextAsync(file, new UTF8Encoding(false), ct).ConfigureAwait(false));
            for (var i = 0; i < batches.Count; i++)
            {
                var description = batches.Count == 1 ? $"Bootstrap {Path.GetFileName(file)}" : $"Bootstrap {Path.GetFileName(file)} (lot {i + 1})";
                await sql.ExecuteAsync(connection, description, batches[i], ct).ConfigureAwait(false);
            }
        }

        return files.Count;
    }

    /// <summary>Desparte scriptul in loturi la randurile care contin doar „GO” (8.5).</summary>
    public static IReadOnlyList<string> SplitBatches(string script) =>
        GoLine().Split(script)
            .Select(batch => batch.Trim())
            .Where(batch => batch.Length > 0)
            .ToList();

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GoLine();
}
