using System.Text;
using Erp.DevDatabase.Sync;
using Erp.Security.Passwords;
using Erp.Setup.Sql.Database;
using Erp.Setup.Tests.Sql;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.DevDatabase;

/// <summary>
/// Baza de dezvoltare care se tine singura la zi (07, „Mediul de dezvoltare”), pe SQL Server real: un depozit temporar,
/// copiat din cel real, pe care se face pe rand fiecare fel de schimbare.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DevDatabaseSyncIntegrationTests(SqlServerFixture sql) : IDisposable
{
    private const string AdminPassword = "Dezvoltare#2026-Parola";
    private const string ProbeMigration = "2099.01.001_Create_DevProbe.sql";

    private readonly string repo = CopyRepository();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Core => Path.Combine("src", "Modules", "Core", "Erp.Core.Infrastructure", "Database");

    public void Dispose() => Directory.Delete(repo, recursive: true);

    /// <summary>Depozitul minim: scripturile Nucleului, ids si hangfire, bootstrap-ul si datele demonstrative.</summary>
    private static string CopyRepository()
    {
        var source = TestPaths.RepositoryRoot();
        var target = Path.Combine(Path.GetTempPath(), "erp-devsync-repo-" + Guid.NewGuid().ToString("N"));
        foreach (var folder in new[]
                 {
                     Core,
                     Path.Combine("src", "Auth", "Erp.Auth", "Database"),
                     Path.Combine("src", "Worker", "Erp.Worker", "Database"),
                     Path.Combine("src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap"),
                 })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(source, folder), "*.sql", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
        }

        Directory.CreateDirectory(Path.Combine(target, "build"));
        File.Copy(Path.Combine(source, "build", "dev-sample-data.sql"), Path.Combine(target, "build", "dev-sample-data.sql"));
        return target;
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(repo, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string Procedure(string body) => $"CREATE OR ALTER PROCEDURE core.usp_DevProbe_Get\nAS\n    SELECT {body} AS Value;\n";

    private DevDatabaseSync Sync(string database, string instance = "DEV") =>
        new(sql.Operator(new LoggerConfiguration().CreateLogger()),
            new DevSyncOptions(database, instance, repo, AdminPassword, Path.Combine(repo, "build", "dev-sample-data.sql")));

    private async Task<T?> Scalar<T>(string database, string query)
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(query, connection);
        return await command.ExecuteScalarAsync(Ct) is T value ? value : default;
    }

    [Fact]
    public async Task Changes_are_applied_one_file_at_a_time_and_a_changed_applied_migration_rebuilds_the_database()
    {
        const string database = "ERP_IT_DEVSYNC";
        await sql.DropTestDatabaseAsync(database);
        Write(Path.Combine(Core, "Migrations", ProbeMigration), "CREATE TABLE core.DevProbe (Id int NOT NULL CONSTRAINT PK_DevProbe PRIMARY KEY);\n");
        var sync = Sync(database);

        // Baza lipseste: se creeaza cu tot ce trebuie pentru lucru — administratorul cu parola configurata si Grupul Exemplu.
        var created = await sync.SyncAsync(Ct);
        Assert.True(created.Succeeded, created.Reason);
        Assert.Equal(SyncAction.Create, created.Action);
        Assert.Equal(0, await Scalar<int>(database, "SELECT COUNT(*) FROM ids.UserAccount WHERE NormalizedUserName = N'ADMINISTRATOR' AND (MustChangePassword = 1 OR TwoFactorRequired = 1);"));
        Assert.True(IdentityPasswordHasher.Verify(
            (await Scalar<string>(database, "SELECT PasswordHash FROM ids.UserAccount WHERE NormalizedUserName = N'ADMINISTRATOR';"))!, AdminPassword));
        Assert.True(await Scalar<int>(database, "SELECT COUNT(*) FROM core.Company;") > 0);
        await sql.ExecuteAsync("INSERT INTO core.DevProbe (Id) VALUES (42);", database);

        Assert.Equal(SyncAction.UpToDate, (await sync.SyncAsync(Ct)).Action);

        // Procedura noua, apoi modificata: doar ea ruleaza, datele raman, jurnalul ii da versiunea noua.
        var procedure = Path.Combine(Core, "Programmability", "Procedures", "core.usp_DevProbe_Get.sql");
        Write(procedure, Procedure("1"));
        var added = await sync.SyncAsync(Ct);
        Assert.Equal((SyncAction.Apply, true), (added.Action, added.Succeeded));
        Write(procedure, Procedure("2"));
        Assert.Equal(SyncAction.Apply, (await sync.SyncAsync(Ct)).Action);
        Assert.Contains("SELECT 2", await Scalar<string>(database, "SELECT OBJECT_DEFINITION(OBJECT_ID(N'core.usp_DevProbe_Get'));"), StringComparison.Ordinal);
        Assert.Equal(2, await Scalar<int>(database, "SELECT COUNT(*) FROM core.ScriptVersion WHERE ScriptName = N'core/Procedures/core.usp_DevProbe_Get.sql';"));

        // O reformatare nu schimba nimic.
        Write(procedure, Procedure("2").Replace("\n", "   \r\n", StringComparison.Ordinal));
        Assert.Equal(SyncAction.UpToDate, (await sync.SyncAsync(Ct)).Action);

        // O migrare noua se aplica fara reconstruire.
        Write(Path.Combine(Core, "Migrations", "2099.01.002_Add_DevProbe_Name.sql"), "ALTER TABLE core.DevProbe ADD Name nvarchar(50) NULL;\n");
        var migrated = await sync.SyncAsync(Ct);
        Assert.Equal((SyncAction.Apply, true), (migrated.Action, migrated.Succeeded));
        Assert.Equal(1, await Scalar<int>(database, "SELECT COUNT(*) FROM core.DevProbe WHERE Id = 42;"));

        // Fisierul procedurii sters: avertisment cu migrarea care lipseste, procedura ramane (08, 5.9).
        File.Delete(Path.Combine(repo, procedure));
        var orphan = await sync.SyncAsync(Ct);
        Assert.Contains(orphan.Warnings, w => w.Contains("DROP PROCEDURE IF EXISTS core.usp_DevProbe_Get", StringComparison.Ordinal));
        Assert.NotEqual(0, await Scalar<int>(database, "SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'core.usp_DevProbe_Get');"));

        // Migrarea deja aplicata se modifica: baza se reface, cu schema noua, administratorul si Grupul Exemplu.
        Write(Path.Combine(Core, "Migrations", ProbeMigration),
            "CREATE TABLE core.DevProbe (Id int NOT NULL CONSTRAINT PK_DevProbe PRIMARY KEY, Code nvarchar(20) NULL);\n");
        var rebuilt = await sync.SyncAsync(Ct);
        Assert.Equal((SyncAction.Rebuild, true), (rebuilt.Action, rebuilt.Succeeded));
        Assert.Contains(ProbeMigration, rebuilt.Reason, StringComparison.Ordinal);
        Assert.Equal(0, await Scalar<int>(database, "SELECT COUNT(*) FROM core.DevProbe;"));
        Assert.NotNull(await Scalar<int?>(database, "SELECT COLUMNPROPERTY(OBJECT_ID(N'core.DevProbe'), N'Code', 'ColumnId');"));
        Assert.Equal(1, await Scalar<int>(database, "SELECT COUNT(*) FROM ids.UserAccount WHERE NormalizedUserName = N'ADMINISTRATOR';"));
        Assert.True(await Scalar<int>(database, "SELECT COUNT(*) FROM core.Company;") > 0);
    }

    [Fact]
    public async Task A_broken_script_is_reported_and_retried_at_the_next_sync()
    {
        const string database = "ERP_IT_DEVSYNC_FAIL";
        await sql.DropTestDatabaseAsync(database);
        var sync = Sync(database);
        Assert.True((await sync.SyncAsync(Ct)).Succeeded);

        var procedure = Path.Combine(Core, "Programmability", "Procedures", "core.usp_DevProbe_Get.sql");
        Write(procedure, "CREATE OR ALTER PROCEDURE core.usp_DevProbe_Get AS SELECT NoSuchColumn FROM core.Company;\n");
        var broken = await sync.SyncAsync(Ct);
        Assert.False(broken.Succeeded);

        Write(procedure, Procedure("1"));
        var fixedResult = await sync.SyncAsync(Ct);
        Assert.Equal((SyncAction.Apply, true), (fixedResult.Action, fixedResult.Succeeded));
        Assert.NotNull(await Scalar<int?>(database, "SELECT OBJECT_ID(N'core.usp_DevProbe_Get');"));
    }

    [Fact]
    public async Task A_database_of_another_instance_is_never_dropped()
    {
        const string database = "ERP_IT_DEVSYNC_OTHER";
        await sql.DropTestDatabaseAsync(database);
        await new DatabaseCreator(sql.Operator()).CreateAsync(new DatabaseCreateOptions(database, "PROD", Guid.NewGuid(), null, null, 256, 256), Ct);
        await sql.ExecuteAsync("CREATE TABLE dbo.KeepMe (Id int NOT NULL);", database);

        // Prima sincronizare ar reface baza, dar ErpInstance = PROD, nu DEV: refuza, baza ramane.
        var result = await Sync(database).SyncAsync(Ct);

        Assert.False(result.Succeeded);
        Assert.Contains("nu o șterg", result.Reason, StringComparison.Ordinal);
        Assert.NotNull(await Scalar<int?>(database, "SELECT OBJECT_ID(N'dbo.KeepMe');"));
    }

    [Fact]
    public async Task A_saved_file_reaches_the_database_while_the_application_runs()
    {
        const string database = "ERP_IT_DEVSYNC_WATCH";
        await sql.DropTestDatabaseAsync(database);
        var sync = Sync(database);
        Assert.True((await sync.SyncAsync(Ct)).Succeeded);

        using var watcher = new DevDatabaseWatcher(sync, repo, new LoggerConfiguration().CreateLogger());
        await watcher.StartAsync(Ct);
        try
        {
            Write(Path.Combine(Core, "Programmability", "Procedures", "core.usp_DevProbe_Get.sql"), Procedure("7"));

            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (await Scalar<int?>(database, "SELECT OBJECT_ID(N'core.usp_DevProbe_Get');") is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250, Ct);
            }

            Assert.NotNull(await Scalar<int?>(database, "SELECT OBJECT_ID(N'core.usp_DevProbe_Get');"));
        }
        finally
        {
            await watcher.StopAsync(CancellationToken.None);
        }
    }
}
