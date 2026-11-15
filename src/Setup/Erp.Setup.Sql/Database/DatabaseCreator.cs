using Erp.Setup.Core;
using Erp.Setup.Sql.Probes;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Sql.Database;

public sealed record DatabaseCreateOptions(
    string Name,
    string Instance,
    Guid InstallId,
    string? DataFilePath,
    string? LogFilePath,
    int InitialDataSizeMb,
    int InitialLogSizeMb);

public enum DatabaseCreateOutcome
{
    /// <summary>Baza a fost creata acum.</summary>
    Created,

    /// <summary>Baza fusese creata de aceeasi instalare (ErpInstallId egal); setarile au fost reaplicate (8.1, reluare).</summary>
    Resumed,

    /// <summary>Baza exista goala, cu collation-ul corect, fara ErpInstallId (C-15 avertisment); a fost preluata.</summary>
    AdoptedEmpty,
}

/// <summary>Pasul I-06 (8.2): creeaza baza case sensitive cu setarile ei si o verifica. Nu suprascrie nimic (R7) si e idempotent (R8).</summary>
public sealed class DatabaseCreator(SqlOperator sql)
{
    public async Task<DatabaseCreateOutcome> CreateAsync(DatabaseCreateOptions options, CancellationToken ct)
    {
        DatabaseScripts.ValidateDatabaseName(options.Name);
        var name = options.Name;
        DatabaseCreateOutcome outcome;

        await using (var master = await sql.OpenAsync("master", ct).ConfigureAwait(false))
        {
            var existing = await ReadExistingAsync(name, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                outcome = Classify(name, options.InstallId, existing);
                sql.Log.Information("Baza {Database} există ({Outcome}); se reaplică setările", name, outcome);
            }
            else
            {
                var info = await ReadDefaultsAsync(master, ct).ConfigureAwait(false);
                var dataFolder = options.DataFilePath ?? info.DataPath
                    ?? throw new SqlSetupException("Calea implicită de date a instanței SQL nu este cunoscută; completați sql.dataFilePath.");
                var logFolder = options.LogFilePath ?? info.LogPath
                    ?? throw new SqlSetupException("Calea implicită de log a instanței SQL nu este cunoscută; completați sql.logFilePath.");
                DatabaseScripts.ValidatePath(dataFolder);
                DatabaseScripts.ValidatePath(logFolder);
                var dataFile = DatabaseScripts.CombinePath(dataFolder, name + ".mdf");
                var logFile = DatabaseScripts.CombinePath(logFolder, name + "_log.ldf");

                foreach (var file in new[] { dataFile, logFile })
                {
                    if (await FileExistsAsync(master, file, ct).ConfigureAwait(false))
                    {
                        throw new SqlSetupException(
                            $"Fișierul {file} există deja pe serverul SQL (rămas de la o bază ștearsă). Installer-ul nu îl suprascrie; mutați-l sau ștergeți-l manual.");
                    }
                }

                await sql.ExecuteAsync(master, $"Creare bază {name} ({DatabaseScripts.Collation})",
                    DatabaseScripts.CreateDatabase(name, dataFile, logFile, options.InitialDataSizeMb, options.InitialLogSizeMb), ct).ConfigureAwait(false);
                outcome = DatabaseCreateOutcome.Created;
            }

            foreach (var (description, statement) in DatabaseScripts.Settings(name))
            {
                await sql.ExecuteAsync(master, $"Bază {name}: {description}", statement, ct).ConfigureAwait(false);
            }

            // ALTER AUTHORIZATION: sa poate fi redenumit, deci se foloseste SUSER_SNAME(0x01).
            var owner = await SqlOperator.ScalarAsync<string>(master, "SELECT SUSER_SNAME(0x01);", ct).ConfigureAwait(false)
                ?? throw new SqlSetupException("Contul sa (SID 0x01) nu a fost găsit.");
            await sql.ExecuteAsync(master, $"Bază {name}: proprietar {owner}", DatabaseScripts.AlterAuthorization(name, owner), ct).ConfigureAwait(false);
        }

        await using (var database = await sql.OpenAsync(name, ct).ConfigureAwait(false))
        {
            await UpsertPropertyAsync(database, "ErpProduct", "ERP", ct).ConfigureAwait(false);
            await UpsertPropertyAsync(database, "ErpInstance", options.Instance, ct).ConfigureAwait(false);
            if (outcome != DatabaseCreateOutcome.Resumed)
            {
                await UpsertPropertyAsync(database, "ErpInstallId", options.InstallId.ToString(), ct).ConfigureAwait(false);
            }
        }

        await VerifyAsync(name, ct).ConfigureAwait(false);
        return outcome;
    }

    private static DatabaseCreateOutcome Classify(string name, Guid installId, Core.Checks.DatabaseInfo existing)
    {
        if (!string.Equals(existing.Collation, DatabaseScripts.Collation, StringComparison.Ordinal))
        {
            throw new SqlSetupException(
                $"Baza {name} există cu collation {existing.Collation}; este necesar {DatabaseScripts.Collation}.", ExitCodes.WrongDatabaseCollation);
        }

        if (string.Equals(existing.ErpInstallId, installId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return DatabaseCreateOutcome.Resumed;
        }

        if (existing.ErpInstallId is not null || existing.UserTableCount > 0)
        {
            throw new SqlSetupException(
                $"Baza {name} există și nu a fost creată de această instalare; instalarea nouă nu o suprascrie.", ExitCodes.DatabaseExistsNotEmpty);
        }

        return DatabaseCreateOutcome.AdoptedEmpty;
    }

    /// <summary>Verificarea dupa creare (8.2); pasul esueaza daca oricare valoare difera.</summary>
    public async Task VerifyAsync(string name, CancellationToken ct)
    {
        const string query = """
            SELECT d.collation_name, d.is_read_committed_snapshot_on, d.recovery_model_desc, d.compatibility_level, d.page_verify_option_desc
            FROM sys.databases AS d
            WHERE d.name = @DatabaseName;
            """;
        await using var master = await sql.OpenAsync("master", ct).ConfigureAwait(false);
        await using var command = new SqlCommand(query, master);
        command.Parameters.Add(new SqlParameter("@DatabaseName", System.Data.SqlDbType.NVarChar, 128) { Value = name });
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new SqlSetupException($"Baza {name} nu există după creare.");
        }

        var problems = new List<string>();
        Expect(problems, "collation", reader.GetString(0), DatabaseScripts.Collation);
        Expect(problems, "READ_COMMITTED_SNAPSHOT", reader.GetBoolean(1).ToString(), bool.TrueString);
        Expect(problems, "recovery", reader.GetString(2), "FULL");
        Expect(problems, "compatibilitate", reader.GetByte(3).ToString(System.Globalization.CultureInfo.InvariantCulture), "160");
        Expect(problems, "PAGE_VERIFY", reader.GetString(4), "CHECKSUM");
        if (problems.Count > 0)
        {
            throw new SqlSetupException($"Baza {name} nu are setările cerute: {string.Join("; ", problems)}.");
        }

        sql.Log.Information("Bază {Database} verificată: {Collation}, RCSI, FULL, 160, CHECKSUM", name, DatabaseScripts.Collation);
    }

    private static void Expect(List<string> problems, string what, string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            problems.Add($"{what} = {actual} (așteptat {expected})");
        }
    }

    private async Task<Core.Checks.DatabaseInfo?> ReadExistingAsync(string name, CancellationToken ct)
    {
        await using var probe = new SqlServerProbe(await sql.OpenAsync("master", ct).ConfigureAwait(false));
        return await probe.GetDatabaseAsync(name, ct).ConfigureAwait(false);
    }

    private static async Task<(string? DataPath, string? LogPath)> ReadDefaultsAsync(SqlConnection master, CancellationToken ct)
    {
        var data = await SqlOperator.ScalarAsync<string>(master, "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260));", ct).ConfigureAwait(false);
        var log = await SqlOperator.ScalarAsync<string>(master, "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(260));", ct).ConfigureAwait(false);
        return (data, log);
    }

    private static async Task<bool> FileExistsAsync(SqlConnection master, string path, CancellationToken ct) =>
        await SqlOperator.ScalarAsync<bool>(master, "SELECT CAST(f.file_exists AS bit) FROM sys.dm_os_file_exists(@Path) AS f;", ct,
            new SqlParameter("@Path", System.Data.SqlDbType.NVarChar, 260) { Value = path }).ConfigureAwait(false);

    private Task UpsertPropertyAsync(SqlConnection database, string name, string value, CancellationToken ct) =>
        sql.ExecuteAsync(database, $"Proprietatea extinsă {name}", DatabaseScripts.UpsertExtendedProperty, ct,
            new SqlParameter("@Name", System.Data.SqlDbType.NVarChar, 128) { Value = name },
            new SqlParameter("@Value", System.Data.SqlDbType.NVarChar, 200) { Value = value });
}
