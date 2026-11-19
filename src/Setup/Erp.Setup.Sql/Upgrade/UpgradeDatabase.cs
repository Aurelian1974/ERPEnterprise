using Dapper;
using Erp.Setup.Core;
using Erp.Setup.Sql.Database;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Upgrade;

/// <summary>Un fisier de date al bazei, pentru snapshot (sys.database_files, type = 0).</summary>
public sealed record DataFile(string LogicalName, string PhysicalName);

/// <summary>Ce se stie despre baza inainte de actualizare (U-01).</summary>
/// <param name="UsedBytes">Spatiul folosit de baza (fisierele de date si log).</param>
/// <param name="FreeBytesOnDataVolume">Spatiul liber pe volumul fisierelor de date (sys.dm_os_volume_stats); null daca nu se poate citi.</param>
/// <param name="LastFullBackupSeconds">Durata ultimului backup complet (msdb.dbo.backupset); null fara istoric.</param>
public sealed record UpgradeDatabaseInfo(string? LastMigration, long UsedBytes, long? FreeBytesOnDataVolume, int? LastFullBackupSeconds,
    bool SnapshotPossible, string? CurrentLicenseFileBase64)
{
    /// <summary>Durata estimata (10.1): ultimul backup complet × 1,2 + 5 minute; null fara istoric de backup.</summary>
    public TimeSpan? EstimatedDuration => LastFullBackupSeconds is { } seconds ? TimeSpan.FromSeconds(seconds * 1.2) + TimeSpan.FromMinutes(5) : null;
}

/// <summary>
/// Protectiile bazei la actualizare (08, 10.2, D23): snapshot pentru revenire rapida si backup COPY_ONLY verificat pentru siguranta;
/// mentenanta prin RESTRICTED_USER (aplicatia nu e db_owner). Numele si caile trec prin validare si QuoteName (abaterea SP-06).
/// </summary>
public sealed class UpgradeDatabase(SqlOperator sql)
{
    public static string SnapshotName(string database) => $"{database}_UPG";

    public static string BackupFileName(string database, string fromVersion, string toVersion, DateTimeOffset now) =>
        $"{database}_{fromVersion}_la_{toVersion}_{now.UtcDateTime:yyyyMMdd_HHmmss}.bak";

    public async Task<UpgradeDatabaseInfo> ReadAsync(string database, CancellationToken ct)
    {
        DatabaseScripts.ValidateDatabaseName(database);
        await using var connection = await sql.OpenAsync(database, ct).ConfigureAwait(false);
        var lastMigration = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "IF OBJECT_ID(N'core.SchemaVersion') IS NOT NULL SELECT TOP (1) ScriptName FROM core.SchemaVersion ORDER BY Id DESC;", cancellationToken: ct))
            .ConfigureAwait(false);
        var used = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT CAST(SUM(CAST(size AS bigint)) * 8192 AS bigint) FROM sys.database_files;", cancellationToken: ct)).ConfigureAwait(false);
        long? free = null;
        try
        {
            free = await connection.ExecuteScalarAsync<long?>(new CommandDefinition("""
                SELECT TOP (1) vs.available_bytes
                FROM sys.database_files AS f
                CROSS APPLY sys.dm_os_volume_stats(DB_ID(), f.file_id) AS vs
                WHERE f.type = 0;
                """, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (SqlException)
        {
            // sys.dm_os_volume_stats cere VIEW SERVER STATE; operatorul e sysadmin, deci lipsa ei e doar informativa.
        }

        var backupSeconds = await connection.ExecuteScalarAsync<int?>(new CommandDefinition("""
            SELECT TOP (1) DATEDIFF(SECOND, backup_start_date, backup_finish_date)
            FROM msdb.dbo.backupset
            WHERE database_name = @database AND type = 'D'
            ORDER BY backup_finish_date DESC;
            """, new { database }, cancellationToken: ct)).ConfigureAwait(false);
        var blockers = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT (SELECT COUNT(*) FROM sys.database_files WHERE type = 2)
                 + (SELECT COUNT(*) FROM sys.filegroups WHERE type IN ('FD', 'FX'))
                 + (SELECT COUNT(*) FROM sys.databases WHERE source_database_id = DB_ID());
            """, cancellationToken: ct)).ConfigureAwait(false);
        var license = await connection.ExecuteScalarAsync<byte[]?>(new CommandDefinition("""
            IF COL_LENGTH(N'core.InstallationLicense', N'LicenseFile') IS NOT NULL
                EXEC (N'SELECT TOP (1) LicenseFile FROM core.InstallationLicense WHERE IsCurrent = 1;');
            """, cancellationToken: ct)).ConfigureAwait(false);
        return new UpgradeDatabaseInfo(lastMigration, used, free, backupSeconds, blockers == 0, license is null ? null : Convert.ToBase64String(license));
    }

    /// <summary>U-03: doar membrii db_owner, dbcreator si sysadmin se mai pot conecta; sesiunile aplicatiei se inchid.</summary>
    public async Task SetRestrictedUserAsync(string database, CancellationToken ct) =>
        await ExecuteMasterAsync($"Mentenanță {database}", $"ALTER DATABASE {Quote(database)} SET RESTRICTED_USER WITH ROLLBACK IMMEDIATE;", ct).ConfigureAwait(false);

    public async Task SetMultiUserAsync(string database, CancellationToken ct) =>
        await ExecuteMasterAsync($"Acces normal {database}", $"ALTER DATABASE {Quote(database)} SET MULTI_USER;", ct).ConfigureAwait(false);

    /// <summary>U-04: backup COPY_ONLY (nu rupe lantul backup-urilor programate), apoi RESTORE VERIFYONLY.</summary>
    public async Task BackupAsync(string database, string file, CancellationToken ct)
    {
        DatabaseScripts.ValidatePath(file);
        await ExecuteMasterAsync($"Backup de siguranță {file}",
            $"BACKUP DATABASE {Quote(database)} TO DISK = {SqlText.SqlLiteral(file)} WITH COPY_ONLY, CHECKSUM, COMPRESSION, INIT;", ct).ConfigureAwait(false);
        await ExecuteMasterAsync($"Verificarea backup-ului {file}", $"RESTORE VERIFYONLY FROM DISK = {SqlText.SqlLiteral(file)} WITH CHECKSUM;", ct)
            .ConfigureAwait(false);
    }

    /// <summary>Folderul UPGRADE\, creat de contul serviciului SQL Server (ca la C-16).</summary>
    public async Task CreateFolderAsync(string folder, CancellationToken ct)
    {
        DatabaseScripts.ValidatePath(folder);
        await ExecuteMasterAsync($"Folderul {folder}", $"EXEC master.dbo.xp_create_subdir {SqlText.SqlLiteral(folder)};", ct).ConfigureAwait(false);
    }

    /// <summary>U-10: backup-urile UPGRADE mai vechi decat ultimele 3 se sterg (xp_delete_files, SQL Server 2019+).</summary>
    public async Task DeleteFileAsync(string file, CancellationToken ct)
    {
        DatabaseScripts.ValidatePath(file);
        await ExecuteMasterAsync($"Ștergerea {file}", $"EXEC master.sys.xp_delete_files {SqlText.SqlLiteral(file)};", ct).ConfigureAwait(false);
    }

    /// <summary>U-04: snapshot-ul, un fisier .ss langa fiecare fisier de date.</summary>
    public async Task CreateSnapshotAsync(string database, CancellationToken ct)
    {
        IReadOnlyList<DataFile> files;
        await using (var connection = await sql.OpenAsync(database, ct).ConfigureAwait(false))
        {
            files = [.. await connection.QueryAsync<DataFile>(new CommandDefinition(
                "SELECT name AS LogicalName, physical_name AS PhysicalName FROM sys.database_files WHERE type = 0 ORDER BY file_id;", cancellationToken: ct))
                .ConfigureAwait(false)];
        }

        await ExecuteMasterAsync($"Snapshot {SnapshotName(database)}", SnapshotScript(database, files), ct).ConfigureAwait(false);
    }

    public static string SnapshotScript(string database, IReadOnlyList<DataFile> files)
    {
        var snapshot = SnapshotName(database);
        var entries = files.Select(f =>
        {
            var folder = PathFolder(f.PhysicalName);
            var path = $"{folder}{snapshot}_{f.LogicalName}.ss";
            DatabaseScripts.ValidatePath(path);
            return $"(NAME = {SqlText.QuoteName(f.LogicalName)}, FILENAME = {SqlText.SqlLiteral(path)})";
        });
        return $"CREATE DATABASE {Quote(snapshot)} ON {string.Join(", ", entries)} AS SNAPSHOT OF {Quote(database)};";
    }

    public async Task<bool> SnapshotExistsAsync(string database, CancellationToken ct)
    {
        await using var master = await sql.OpenAsync("master", ct).ConfigureAwait(false);
        return await SqlOperator.ScalarAsync<int>(master, "SELECT COUNT(*) FROM sys.databases WHERE name = @name AND source_database_id IS NOT NULL;", ct,
            new SqlParameter("@name", SnapshotName(database))).ConfigureAwait(false) > 0;
    }

    public async Task DropSnapshotAsync(string database, CancellationToken ct) =>
        await ExecuteMasterAsync($"Ștergerea snapshot-ului {SnapshotName(database)}", $"DROP DATABASE IF EXISTS {Quote(SnapshotName(database))};", ct)
            .ConfigureAwait(false);

    /// <summary>R-02, varianta rapida: baza revine la starea din snapshot.</summary>
    public async Task RestoreFromSnapshotAsync(string database, CancellationToken ct) =>
        await ExecuteMasterAsync($"Revenirea {database} din snapshot", $"""
            ALTER DATABASE {Quote(database)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            RESTORE DATABASE {Quote(database)} FROM DATABASE_SNAPSHOT = {SqlText.SqlLiteral(SnapshotName(database))};
            ALTER DATABASE {Quote(database)} SET MULTI_USER;
            """, ct).ConfigureAwait(false);

    /// <summary>R-02, varianta sigura: snapshot-ul se sterge (o baza cu snapshot nu se poate restaura), apoi backup-ul U-04.</summary>
    public async Task RestoreFromBackupAsync(string database, string file, CancellationToken ct)
    {
        DatabaseScripts.ValidatePath(file);
        await DropSnapshotAsync(database, ct).ConfigureAwait(false);
        await ExecuteMasterAsync($"Revenirea {database} din backup", $"""
            ALTER DATABASE {Quote(database)} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            RESTORE DATABASE {Quote(database)} FROM DISK = {SqlText.SqlLiteral(file)} WITH REPLACE, CHECKSUM, RECOVERY;
            ALTER DATABASE {Quote(database)} SET MULTI_USER;
            """, ct).ConfigureAwait(false);
    }

    private async Task ExecuteMasterAsync(string description, string text, CancellationToken ct)
    {
        await using var master = await sql.OpenAsync("master", ct).ConfigureAwait(false);
        await sql.ExecuteAsync(master, description, text, ct).ConfigureAwait(false);
    }

    private static string Quote(string database)
    {
        DatabaseScripts.ValidateDatabaseName(database);
        return SqlText.QuoteName(database);
    }

    /// <summary>Folderul unei cai SQL Server, cu separatorul ei (Windows sau Linux).</summary>
    private static string PathFolder(string physicalName)
    {
        var index = Math.Max(physicalName.LastIndexOf('\\'), physicalName.LastIndexOf('/'));
        return index < 0 ? string.Empty : physicalName[..(index + 1)];
    }
}
