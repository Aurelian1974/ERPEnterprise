using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Dapper;
using Erp.Configuration;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.State;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Logins;

namespace Erp.Setup.Engine;

/// <summary>„Dezinstalează” (08, 11.6): nu sterge baza, backup-urile si joburile (R9); loginurile aplicatiei se dezactiveaza.</summary>
public static class Uninstaller
{
    public sealed record Result(IReadOnlyList<string> Removed, IReadOnlyList<string> Kept);

    public static async Task<Result> RunAsync(InstallRequest request, InstanceState state, SqlOperator sql, IWindowsSetup windows, CancellationToken ct)
    {
        var removed = windows.RemoveApplication(request).ToList();
        if (state.AppSqlLogins.Count > 0)
        {
            await new LoginManager(sql).DisableAsync(state.AppSqlLogins, ct).ConfigureAwait(false);
            removed.Add($"loginurile SQL dezactivate: {string.Join(", ", state.AppSqlLogins)}");
        }

        // instance.json ramane, redenumit, ca referinta (amprentele certificatelor, baza); ecranul 0 nu mai arata instalarea.
        var paths = InstanceDataPaths.For(request.DataRoot, request.Instance);
        File.Move(paths.InstanceJson, Path.Combine(paths.InstanceDirectory, "instance.uninstalled.json"), overwrite: true);

        var kept = new List<string>
        {
            $"baza {state.DatabaseName} pe {state.SqlServer}, backup-urile din {state.BackupFolder ?? "—"} și joburile de backup",
            $"{paths.InstanceDirectory} (jurnale, install\\answers.json)",
            $"certificatul de protecție a datelor {state.DataProtectionCertificateThumbprint} (LocalMachine\\My) – necesar la o reinstalare peste aceeași bază",
            $"certificatul HTTPS {state.CertificateThumbprint} (LocalMachine\\My)",
        };
        return new Result(removed, kept);
    }
}

/// <summary>„Resetează accesul administratorului” (08, 11.3).</summary>
public static class AdministratorAccess
{
    public static Task<string> ResetAsync(InstanceState state, SqlOperator sql, string setupVersion, CancellationToken ct) =>
        new AdministratorReset(sql).ResetAsync(state.DatabaseName ?? throw new InvalidOperationException("instance.json nu are baza."),
            InstallRequest.AdministratorUserName, setupVersion, Environment.MachineName, ct);
}

/// <summary>
/// „Pachet de diagnostic” (08, 11.5): fara cheia de recuperare, inelul de chei, fisierul de licenta, initial-admin.txt si date de
/// business. Partile care nu se pot citi (SQL oprit, aplicatia oprita) se noteaza in pachet, nu opresc crearea lui.
/// </summary>
public static class DiagnosticPackage
{
    public static async Task<string> CreateAsync(InstallRequest request, InstanceState state, SqlOperator sql, IWindowsSetup windows, string outputFolder,
        TimeProvider time, CancellationToken ct)
    {
        var paths = InstanceDataPaths.For(request.DataRoot, request.Instance);
        var now = time.GetUtcNow();
        Directory.CreateDirectory(outputFolder);
        var zipPath = Path.Combine(outputFolder, $"erp-diagnostic-{request.Instance}-{now.UtcDateTime:yyyyMMdd-HHmmss}.zip");
        await using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        AddFile(zip, paths.InstanceJson, "instance.json");
        AddFile(zip, paths.AppSettings, "appsettings.Installation.json");
        AddFile(zip, paths.Answers, "answers.json");
        if (Directory.Exists(paths.LogsDirectory))
        {
            var since = now.AddDays(-7).UtcDateTime;
            foreach (var log in Directory.EnumerateFiles(paths.LogsDirectory, "*", SearchOption.AllDirectories)
                .Where(f => File.GetLastWriteTimeUtc(f) >= since && !f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
            {
                AddFile(zip, log, Path.Combine("logs", Path.GetRelativePath(paths.LogsDirectory, log)).Replace('\\', '/'));
            }
        }

        AddText(zip, "sql.txt", await SqlSummaryAsync(state, sql, ct).ConfigureAwait(false));
        try
        {
            var pinned = state.CertificateKind == CertificateKind.Internal ? state.CertificateThumbprint : null;
            var health = await windows.CheckHealthAsync(request, state.CurrentVersion ?? string.Empty, state.LastMigration, pinned, ct).ConfigureAwait(false);
            AddText(zip, "health.json", JsonSerializer.Serialize(health, InstanceStateStore.JsonOptions));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AddText(zip, "health.json", $"Health-check-ul nu a putut fi citit: {ex.Message}");
        }

        return zipPath;
    }

    private static async Task<string> SqlSummaryAsync(InstanceState state, SqlOperator sql, CancellationToken ct)
    {
        var text = new StringBuilder();
        try
        {
            await using var connection = await sql.OpenAsync(state.DatabaseName!, ct).ConfigureAwait(false);
            var server = await connection.QuerySingleAsync(new CommandDefinition(
                "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)) AS Version, CAST(SERVERPROPERTY('Edition') AS nvarchar(200)) AS Edition, " +
                "CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(200)) AS Collation;", cancellationToken: ct)).ConfigureAwait(false);
            text.AppendLine($"SQL Server {server.Version}, {server.Edition}, collation {server.Collation}");
            Section(text, "Ultimele 20 de migrații", await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT TOP (20) CONCAT(ScriptName, N' · ', CONVERT(nvarchar(30), Applied, 120)) FROM core.SchemaVersion ORDER BY Id DESC;", cancellationToken: ct))
                .ConfigureAwait(false));
            Section(text, "Licența (rezumat)", await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT CONCAT(LicenseId, N' · ', CustomerName, N' · ', LimitCompanies, N' firme · ', Modules, N' · până la ', COALESCE(CONVERT(nvarchar(10), ValidUntil, 23), N'—')) " +
                "FROM core.InstallationLicense WHERE IsCurrent = 1;", cancellationToken: ct)).ConfigureAwait(false));
            Section(text, "Ultimele 50 de rulări ale joburilor ERP", await connection.QueryAsync<string>(new CommandDefinition("""
                SELECT TOP (50) CONCAT(j.name, N' · ', msdb.dbo.agent_datetime(h.run_date, h.run_time), N' · ', h.run_status, N' · ', LEFT(h.message, 200))
                FROM msdb.dbo.sysjobhistory AS h JOIN msdb.dbo.sysjobs AS j ON j.job_id = h.job_id
                WHERE j.name LIKE N'ERP %' AND h.step_id = 0
                ORDER BY h.instance_id DESC;
                """, cancellationToken: ct)).ConfigureAwait(false));
            Section(text, "Ultimele 20 de backup-uri", await connection.QueryAsync<string>(new CommandDefinition("""
                SELECT TOP (20) CONCAT(type, N' · ', CONVERT(nvarchar(30), backup_finish_date, 120), N' · ', backup_size / 1048576, N' MB')
                FROM msdb.dbo.backupset WHERE database_name = DB_NAME() ORDER BY backup_finish_date DESC;
                """, cancellationToken: ct)).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            text.AppendLine($"Datele SQL nu au putut fi citite: {ex.Message}");
        }

        return text.ToString();
    }

    private static void Section(StringBuilder text, string title, IEnumerable<string> rows)
    {
        text.AppendLine().AppendLine(title + ":");
        foreach (var row in rows)
        {
            text.AppendLine("  " + row);
        }
    }

    private static void AddFile(ZipArchive zip, string path, string name)
    {
        if (!File.Exists(path))
        {
            return;
        }

        // Fisierele de jurnal pot fi deschise de aplicatie; se citesc cu partajare.
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        source.CopyTo(target);
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
