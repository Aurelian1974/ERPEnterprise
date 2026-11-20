using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace Erp.DevDatabase.Sync;

/// <summary>
/// Versiunea scripturilor din istoricul Git, cu aceeasi regula ca build/KitBuilder.psm1 (Get-KitScriptVersions, 08 5.9):
/// de cate ori s-a schimbat fisierul (git log --follow), plus 1 daca are modificari necomise. Asa, o procedura modificata
/// local are deja versiunea pe care o va avea dupa commit. Fara Git, scripturile primesc versiunea 0, cu avertisment.
/// </summary>
public static class GitScriptVersions
{
    public static IReadOnlyDictionary<string, int> Compute(string repoRoot, IEnumerable<RepositoryScript> scripts, ILogger log)
    {
        var versions = new Dictionary<string, int>(StringComparer.Ordinal);
        // Un depozit fara niciun commit nu are istoric: git log ar esua, deci toate fisierele au 0 commit-uri.
        var hasCommits = Git(repoRoot, log: null, "rev-parse", "--verify", "-q", "HEAD") is not null;
        foreach (var script in scripts)
        {
            var path = Path.GetRelativePath(repoRoot, script.SourcePath).Replace('\\', '/');
            var commits = hasCommits ? Git(repoRoot, log, "log", "--follow", "--format=%H", "--", path) : string.Empty;
            var pending = Git(repoRoot, log, "status", "--porcelain", "--", path);
            if (commits is null || pending is null)
            {
                return new Dictionary<string, int>(StringComparer.Ordinal);
            }

            versions[script.JournalName] = Lines(commits) + (Lines(pending) > 0 ? 1 : 0);
        }

        return versions;
    }

    private static int Lines(string output) => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

    /// <param name="log">Null cand un cod de iesire nenul e un raspuns asteptat (rev-parse fara commit-uri), nu o problema.</param>
    private static string? Git(string repoRoot, ILogger? log, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repoRoot, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                return output;
            }

            log?.Warning("Git a întors codul {ExitCode}; versiunile scripturilor rămân 0 la această sincronizare.", process.ExitCode);
            return null;
        }
        catch (Win32Exception)
        {
            log?.Warning("Git nu este disponibil; versiunile scripturilor rămân 0 la această sincronizare.");
            return null;
        }
    }
}
