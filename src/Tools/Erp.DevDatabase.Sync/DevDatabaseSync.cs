using System.Data;
using System.Text.RegularExpressions;
using Erp.Security.Passwords;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Database;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.DevDatabase.Sync;

/// <param name="Database">Baza de dezvoltare (de exemplu ERP_DEV).</param>
/// <param name="Instance">Instanta de dezvoltare (proprietatea ErpInstance a bazei); o baza cu alta instanta nu se sterge niciodata.</param>
/// <param name="RepoRoot">Radacina depozitului (Erp.slnx).</param>
/// <param name="AdminPassword">Parola administratorului dupa reconstruire, din user-secrets (DevDatabase:AdminPassword); fara ea, una temporara.</param>
/// <param name="SampleDataPath">Datele demonstrative reincarcate dupa reconstruire (build/dev-sample-data.sql); null = fara.</param>
public sealed record DevSyncOptions(string Database, string Instance, string RepoRoot, string? AdminPassword, string? SampleDataPath);

public sealed record DevSyncResult(SyncAction Action, string Reason, bool Succeeded, IReadOnlyList<string> Warnings);

/// <summary>
/// Tine baza de dezvoltare la zi cu scripturile din depozit (07, „Mediul de dezvoltare”): aplica doar ce s-a schimbat, cu
/// aceiasi pasi SQL ca installer-ul (Erp.Setup.Sql: bootstrap, DbUp, jurnalul de versiuni core.ScriptVersion), si reface
/// baza cand o migrare deja aplicata s-a modificat. O sincronizare ruleaza pe rand cu celelalte (pornire si observator).
/// </summary>
public sealed partial class DevDatabaseSync(SqlOperator sql, DevSyncOptions options)
{
    /// <summary>Amprenta scripturilor de bootstrap, ca proprietate extinsa a bazei de dezvoltare (nu exista in kit).</summary>
    public const string BootstrapHashProperty = "ErpDevBootstrapHash";

    private const string ProductVersion = "dev";

    private readonly SemaphoreSlim gate = new(1, 1);

    private ILogger Log => sql.Log;

    public async Task<DevSyncResult> SyncAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SyncCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<DevSyncResult> SyncCoreAsync(CancellationToken ct)
    {
        var plan = new SyncPlan(SyncAction.UpToDate, "nepornită", false, new HashSet<string>(), new HashSet<string>(), []);
        try
        {
            if (!DatabaseName().IsMatch(options.Database))
            {
                throw new InvalidOperationException($"Numele bazei de dezvoltare „{options.Database}” este invalid.");
            }

            using var staging = new StagedScripts(options.RepoRoot);
            var scripts = KitScripts.Load(staging.Root);
            var repository = RepositoryScripts.Enumerate(options.RepoRoot);
            var states = scripts.Migrations.Select(s => State(ScriptKind.Migration, s))
                .Concat(scripts.Programmability.Select(s => State(ScriptKind.Programmability, s)))
                .Concat(scripts.Seed.Select(s => State(ScriptKind.Seed, s)))
                .ToArray();
            var bootstrapHash = BootstrapHash();
            var database = await ReadStateAsync(ct).ConfigureAwait(false);
            plan = SyncPlanner.Plan(states, bootstrapHash, database);

            switch (plan.Action)
            {
                case SyncAction.UpToDate:
                    Log.Debug("Baza de dezvoltare {Database} este la zi.", options.Database);
                    break;
                case SyncAction.Create:
                case SyncAction.Rebuild:
                    Log.Warning("Baza de dezvoltare {Database} se reconstruiește: {Reason}.", options.Database, plan.Reason);
                    await BuildAsync(scripts, repository, bootstrapHash, dropExisting: plan.Action == SyncAction.Rebuild, ct).ConfigureAwait(false);
                    Log.Information("Baza de dezvoltare {Database} a fost reconstruită.", options.Database);
                    break;
                case SyncAction.Apply:
                    Log.Information("Baza de dezvoltare {Database}: {Reason}.", options.Database, plan.Reason);
                    Apply(plan, scripts, repository);
                    break;
            }

            var warnings = await OrphanWarningsAsync(plan.Orphans, ct).ConfigureAwait(false);
            foreach (var warning in warnings)
            {
                Log.Warning("{Warning}", warning);
            }

            return new DevSyncResult(plan.Action, plan.Reason, true, warnings);
        }
        catch (Exception ex) when (ex is SqlException or SqlSetupException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // Eroarea se vede in consola; amprenta scriptului esuat nu s-a scris, deci urmatoarea salvare il reincearca.
            Log.Error("Sincronizarea bazei de dezvoltare {Database} a eșuat: {Message}", options.Database, ex.Message);
            return new DevSyncResult(plan.Action, ex.Message, false, []);
        }
    }

    private static ScriptState State(ScriptKind kind, DbUp.Engine.SqlScript script) => new(kind, script.Name, ScriptFingerprint.Compute(script.Contents));

    private string BootstrapHash()
    {
        var folder = RepositoryScripts.BootstrapDirectory(options.RepoRoot);
        var parts = Directory.EnumerateFiles(folder, "*.sql")
            .Order(StringComparer.Ordinal)
            .Select(file => Path.GetFileName(file) + ":" + ScriptFingerprint.Compute(File.ReadAllText(file)));
        return ScriptFingerprint.Compute(string.Join('\n', parts));
    }

    private async Task<DatabaseState> ReadStateAsync(CancellationToken ct)
    {
        await using (var master = await sql.OpenAsync("master", ct).ConfigureAwait(false))
        {
            var id = await SqlOperator.ScalarAsync<int?>(master, "SELECT CAST(DB_ID(@Name) AS int);", ct, new SqlParameter("@Name", options.Database)).ConfigureAwait(false);
            if (id is null)
            {
                return DatabaseState.Missing;
            }
        }

        await using var connection = await sql.OpenAsync(options.Database, ct).ConfigureAwait(false);
        var hasJournal = await SqlOperator.ScalarAsync<int?>(connection, "SELECT OBJECT_ID(N'core.ScriptVersion', N'U');", ct).ConfigureAwait(false) is not null;
        var bootstrapHash = await SqlOperator.ScalarAsync<string>(connection,
            "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = @Name;", ct,
            new SqlParameter("@Name", BootstrapHashProperty)).ConfigureAwait(false);

        var applied = new HashSet<string>(StringComparer.Ordinal);
        if (await SqlOperator.ScalarAsync<int?>(connection, "SELECT OBJECT_ID(N'core.SchemaVersion', N'U');", ct).ConfigureAwait(false) is not null)
        {
            await foreach (var row in ReadAsync(connection, "SELECT ScriptName FROM core.SchemaVersion;", ct))
            {
                applied.Add(row.GetString(0));
            }
        }

        var recorded = new Dictionary<string, (ScriptKind, string)>(StringComparer.Ordinal);
        if (hasJournal)
        {
            const string latest = """
                SELECT v.ScriptName, v.ScriptKind, v.ContentHash
                FROM core.ScriptVersion AS v
                WHERE v.Id = (SELECT MAX(l.Id) FROM core.ScriptVersion AS l WHERE l.ScriptName = v.ScriptName);
                """;
            await foreach (var row in ReadAsync(connection, latest, ct))
            {
                recorded[row.GetString(0)] = ((ScriptKind)row.GetByte(1), row.GetString(2));
            }
        }

        return new DatabaseState(true, hasJournal, bootstrapHash, applied, recorded);
    }

    private static async IAsyncEnumerable<SqlDataReader> ReadAsync(SqlConnection connection, string query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            yield return reader;
        }
    }

    /// <summary>Baza refacuta de la zero: bootstrap, toate scripturile, administratorul, datele demonstrative.</summary>
    private async Task BuildAsync(KitScripts scripts, IReadOnlyList<RepositoryScript> repository, string bootstrapHash, bool dropExisting, CancellationToken ct)
    {
        var installId = dropExisting ? await DropAsync(ct).ConfigureAwait(false) : Guid.NewGuid();
        await new DatabaseCreator(sql).CreateAsync(new DatabaseCreateOptions(options.Database, options.Instance, installId, null, null, 256, 256), ct)
            .ConfigureAwait(false);
        await new BootstrapRunner(sql).RunAsync(options.Database, RepositoryScripts.BootstrapDirectory(options.RepoRoot), ct).ConfigureAwait(false);

        var runner = new MigrationRunner(Log, new ScriptVersions(GitScriptVersions.Compute(options.RepoRoot, repository, Log), ProductVersion));
        var connection = sql.ConnectionString(options.Database);
        runner.RunMigrations(connection, scripts);
        runner.RunProgrammability(connection, scripts);
        runner.RunSeed(connection, scripts);

        await CreateAdministratorAsync(ct).ConfigureAwait(false);
        await LoadSampleDataAsync(ct).ConfigureAwait(false);

        // La final: o reconstruire intrerupta lasa amprenta lipsa, deci urmatoarea sincronizare o reia de la zero.
        await SetBootstrapHashAsync(bootstrapHash, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sterge baza de dezvoltare, dar numai daca proprietatea ei ErpInstance este instanta de dezvoltare configurata: o
    /// configurare gresita care ar arata spre alta baza nu o poate sterge. Intoarce ErpInstallId, pastrat la recreare.
    /// </summary>
    private async Task<Guid> DropAsync(CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(options.Database, ct).ConfigureAwait(false);
        var instance = await SqlOperator.ScalarAsync<string>(connection,
            "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstance';", ct).ConfigureAwait(false);
        if (!string.Equals(instance, options.Instance, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Baza {options.Database} nu aparține instanței de dezvoltare {options.Instance} (ErpInstance = {instance ?? "lipsă"}); nu o șterg.");
        }

        var installId = await SqlOperator.ScalarAsync<string>(connection,
            "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstallId';", ct).ConfigureAwait(false);

        connection.ChangeDatabase("master");
        var name = SqlText.QuoteName(options.Database);
        await sql.ExecuteAsync(connection, "ștergerea bazei de dezvoltare",
            $"ALTER DATABASE {name} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {name};", ct).ConfigureAwait(false);
        SqlConnection.ClearAllPools();
        return Guid.TryParse(installId, out var id) ? id : Guid.NewGuid();
    }

    private void Apply(SyncPlan plan, KitScripts scripts, IReadOnlyList<RepositoryScript> repository)
    {
        var toRun = repository.Where(s => (plan.RunMigrations && s.Kind == ScriptKind.Migration)
            || plan.Programmability.Contains(s.JournalName) || plan.Seed.Contains(s.JournalName));
        var runner = new MigrationRunner(Log, new ScriptVersions(GitScriptVersions.Compute(options.RepoRoot, toRun, Log), ProductVersion));
        var connection = sql.ConnectionString(options.Database);
        if (plan.RunMigrations)
        {
            runner.RunMigrations(connection, scripts);
        }

        if (plan.Programmability.Count > 0)
        {
            runner.RunProgrammability(connection, scripts.Programmability.Where(s => plan.Programmability.Contains(s.Name)));
        }

        if (plan.Seed.Count > 0)
        {
            runner.RunSeed(connection, scripts.Seed.Where(s => plan.Seed.Contains(s.Name)));
        }
    }

    /// <summary>
    /// Administratorul de dezvoltare: cu parola din user-secrets, fara schimbare obligatorie si fara 2FA (aceeasi exceptie ca
    /// utilizatorii E2E, numai pe baza de dezvoltare); fara parola configurata, cea temporara se afiseaza o data in consola.
    /// </summary>
    private async Task CreateAdministratorAsync(CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(options.Database, ct).ConfigureAwait(false);
        var admin = await new InitialAdminCreator(sql).CreateAsync(connection,
            new InitialAdminRequest("administrator", "Administrator ERP", null, ProductVersion, Environment.MachineName), ct).ConfigureAwait(false);
        if (!admin.Created)
        {
            return;
        }

        if (string.IsNullOrEmpty(options.AdminPassword))
        {
            // Doar pe ecran, niciodata in jurnal (08, R8). Parola proprie se configureaza cu dotnet user-secrets.
            Console.WriteLine($"Baza de dezvoltare: administrator, parolă temporară {admin.TemporaryPassword}. " +
                "Pentru o parolă care rămâne după reconstruiri: dotnet user-secrets set DevDatabase:AdminPassword <parola> --project src/Host/Erp.Host");
            return;
        }

        await sql.ExecuteAsync(connection, "parola administratorului de dezvoltare", """
            UPDATE ids.UserAccount
            SET PasswordHash = @PasswordHash, PasswordChangedAt = SYSUTCDATETIME(), MustChangePassword = 0, TwoFactorRequired = 0
            WHERE NormalizedUserName = N'ADMINISTRATOR';
            """, ct, new SqlParameter("@PasswordHash", SqlDbType.NVarChar, 500) { Value = IdentityPasswordHasher.Hash(options.AdminPassword) }).ConfigureAwait(false);
        Log.Information("Administratorul de dezvoltare folosește parola din user-secrets.");
    }

    private async Task LoadSampleDataAsync(CancellationToken ct)
    {
        if (options.SampleDataPath is null || !File.Exists(options.SampleDataPath))
        {
            return;
        }

        // Datele demonstrative nu sunt structura bazei: daca nu se pot incarca (scriptul refuza orice baza care nu e ERP_DEV*),
        // baza ramane construita si marcata la zi; altfel fiecare sincronizare ar reface-o din nou, fara capat.
        try
        {
            await using var connection = await sql.OpenAsync(options.Database, ct).ConfigureAwait(false);
            await using var command = new SqlCommand(await File.ReadAllTextAsync(options.SampleDataPath, ct).ConfigureAwait(false), connection) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            Log.Information("Datele demonstrative (Grupul Exemplu) au fost reîncărcate.");
        }
        catch (SqlException ex)
        {
            Log.Warning("Datele demonstrative nu s-au încărcat în {Database}: {Message}", options.Database, ex.Message);
        }
    }

    private async Task SetBootstrapHashAsync(string hash, CancellationToken ct)
    {
        await using var connection = await sql.OpenAsync(options.Database, ct).ConfigureAwait(false);
        await sql.ExecuteAsync(connection, "amprenta bootstrap-ului", """
            IF EXISTS (SELECT 1 FROM sys.extended_properties WHERE class = 0 AND name = @Name)
                EXEC sys.sp_updateextendedproperty @name = @Name, @value = @Hash;
            ELSE
                EXEC sys.sp_addextendedproperty @name = @Name, @value = @Hash;
            """, ct, new SqlParameter("@Name", BootstrapHashProperty), new SqlParameter("@Hash", hash)).ConfigureAwait(false);
    }

    /// <summary>
    /// Obiectele programabile al caror fisier a disparut, dar care exista inca in baza. Nu se sterg: la clienti ele raman
    /// pana la o migrare DROP … IF EXISTS (08, 5.9), deci avertismentul arata migrarea care lipseste.
    /// </summary>
    private async Task<IReadOnlyList<string>> OrphanWarningsAsync(IReadOnlyList<string> orphans, CancellationToken ct)
    {
        if (orphans.Count == 0)
        {
            return [];
        }

        var warnings = new List<string>();
        await using var connection = await sql.OpenAsync(options.Database, ct).ConfigureAwait(false);
        foreach (var orphan in orphans)
        {
            var parts = orphan.Split('/');
            var objectName = Path.GetFileNameWithoutExtension(parts[^1]);
            var kind = parts.Length == 3 ? parts[1] switch { "Procedures" => "PROCEDURE", "Views" => "VIEW", "Functions" => "FUNCTION", _ => null } : null;
            if (kind is null)
            {
                continue;
            }

            if (await SqlOperator.ScalarAsync<int?>(connection, "SELECT OBJECT_ID(@Name);", ct, new SqlParameter("@Name", objectName)).ConfigureAwait(false) is not null)
            {
                warnings.Add($"{objectName} nu mai are fișier în depozit, dar există în bază; la clienți rămâne până la o migrare cu „DROP {kind} IF EXISTS {objectName};”.");
            }
        }

        return warnings;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{2,63}$")]
    private static partial Regex DatabaseName();

    /// <summary>Scripturile depozitului asezate temporar ca in kit, pentru KitScripts (aceleasi nume si aceeasi ordine ca la client).</summary>
    private sealed class StagedScripts : IDisposable
    {
        public StagedScripts(string repoRoot)
        {
            Directory.CreateDirectory(Root);
            RepositoryScripts.LayOut(repoRoot, Root);
        }

        public string Root { get; } = Path.Combine(Path.GetTempPath(), "erp-devsync-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
