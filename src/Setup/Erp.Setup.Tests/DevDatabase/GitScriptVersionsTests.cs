using System.Diagnostics;
using Erp.DevDatabase.Sync;
using Erp.Setup.Sql.Database;
using Serilog;

namespace Erp.Setup.Tests.DevDatabase;

/// <summary>
/// Versiunea din Git in baza de dezvoltare: aceeasi regula ca Get-KitScriptVersions din build/KitBuilder.psm1 (testata cu
/// Pester), ca o procedura sa aiba acelasi numar in ERP_DEV si la client.
/// </summary>
public sealed class GitScriptVersionsTests : IDisposable
{
    private const string Procedure = "src/Modules/Core/Erp.Core.Infrastructure/Database/Programmability/Procedures/core.usp_Add_Orice.sql";

    private readonly string repo = Path.Combine(Path.GetTempPath(), "erp-git-versions-" + Guid.NewGuid().ToString("N"));

    public GitScriptVersionsTests()
    {
        Directory.CreateDirectory(repo);
        Git("init", "-q");
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(repo, recursive: true);
    }

    private void Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private void Commit(string message)
    {
        Git("add", "-A");
        Git("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "-m", message);
    }

    private void Write(string content)
    {
        var path = Path.Combine(repo, Procedure);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private int Version()
    {
        var script = new RepositoryScript(ScriptKind.Programmability, "core/Procedures/core.usp_Add_Orice.sql",
            "programmability/core/Procedures/core.usp_Add_Orice.sql", Path.Combine(repo, Procedure));
        return GitScriptVersions.Compute(repo, [script], new LoggerConfiguration().CreateLogger())[script.JournalName];
    }

    [Fact]
    public void Adding_is_version_1_and_each_committed_change_adds_one()
    {
        Write("CREATE OR ALTER PROCEDURE core.usp_Add_Orice AS SELECT 1;");
        Commit("adaug");
        Assert.Equal(1, Version());

        Write("CREATE OR ALTER PROCEDURE core.usp_Add_Orice AS SELECT 2;");
        Commit("modific");
        Assert.Equal(2, Version());
    }

    [Fact]
    public void An_uncommitted_change_already_has_the_next_version()
    {
        Write("CREATE OR ALTER PROCEDURE core.usp_Add_Orice AS SELECT 1;");
        Assert.Equal(1, Version());

        Commit("adaug");
        Write("CREATE OR ALTER PROCEDURE core.usp_Add_Orice AS SELECT 2;");
        Assert.Equal(2, Version());
    }
}
