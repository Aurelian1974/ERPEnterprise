using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Agent;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Licensing;
using Erp.Setup.Sql.Logins;
using Microsoft.Data.SqlClient;

namespace Erp.Setup.Install;

/// <summary>
/// Pasii SQL ai instalarii (8.1: I-05…I-14, I-21, I-22). Nu folosesc API-uri Windows, deci se testeaza si in containerul SQL.
/// Fiecare pas compune serviciile din Erp.Setup.Sql; toti sunt idempotenti (R8).
/// </summary>
public static class SqlInstallSteps
{
    /// <param name="createFolders">Folderele din DataRoot (3.2), create de planul Windows; testele SQL le omit.</param>
    public static PlannedStep I05_DataFolders(InstallSession session, Action? createFolders = null) => Step("I-05",
        "Creez folderele instanței și instance.json", ExitCodes.IisServiceOrFileError, (context, _) =>
        {
            var answers = session.RequireAnswers();
            createFolders?.Invoke();
            Directory.CreateDirectory(Path.GetDirectoryName(session.StatePath)!);
            if (session.State is null)
            {
                session.InstallId = Guid.NewGuid();
                session.State = NewState(session) with { CompletedSteps = [.. session.CompletedThisRun] };
                InstanceStateStore.Write(session.StatePath, session.State);
                return Done($"instalare nouă, installId {session.InstallId}");
            }

            context.Log.Information("Reiau instalarea {InstallId} a instanței {Instance}", session.InstallId, answers.Instance);
            return Done($"reluare, installId {session.InstallId}");
        });

    public static PlannedStep I06_Database(InstallSession session) => Step("I-06", "Creez baza de date (case sensitive)", ExitCodes.SqlInstallError,
        async (_, ct) =>
        {
            var sql = session.RequireAnswers().Sql;
            var outcome = await new DatabaseCreator(session.RequireSql()).CreateAsync(
                new DatabaseCreateOptions(sql.DatabaseName, session.RequireAnswers().Instance, session.InstallId, sql.DataFilePath, sql.LogFilePath,
                    sql.InitialDataSizeMb, sql.InitialLogSizeMb), ct);
            return outcome == DatabaseCreateOutcome.AdoptedEmpty
                ? StepResult.Warning($"baza goală existentă {sql.DatabaseName} a fost preluată")
                : StepResult.Done(outcome == DatabaseCreateOutcome.Created ? $"baza {sql.DatabaseName} creată" : $"baza {sql.DatabaseName} verificată");
        });

    public static PlannedStep I07_Bootstrap(InstallSession session) => Step("I-07", "Creez schemele și rolurile bazei", ExitCodes.SqlInstallError,
        async (_, ct) =>
        {
            var count = await new BootstrapRunner(session.RequireSql())
                .RunAsync(session.RequireAnswers().Sql.DatabaseName, Path.Combine(session.DatabaseRoot, "bootstrap"), ct);
            return StepResult.Done($"{count} scripturi");
        });

    public static PlannedStep I08_Logins(InstallSession session) => Step("I-08", "Creez loginurile și utilizatorii SQL", ExitCodes.SqlInstallError,
        async (_, ct) =>
        {
            var answers = session.RequireAnswers();
            var manager = Logins(session);
            // Parola aplicatiei se genereaza la fiecare rulare si se scrie in secrets.app.bin la I-16 (8.4).
            session.AppPassword = answers.Sql.AppAuthentication == SqlAuthentication.Sql ? SqlPasswordGenerator.Generate() : null;
            await manager.EnsureAppLoginAsync(answers.Sql.DatabaseName, answers.Sql.AppAuthentication, session.AppPassword, ct);
            session.Migrator = await manager.EnableMigratorAsync(answers.Sql.DatabaseName, ct);
            return StepResult.Done($"{manager.Principals.AppLogin}, {manager.Principals.MigratorLogin} (activ până la I-14)");
        });

    public static PlannedStep I09_Migrations(InstallSession session) => Step("I-09", "Rulez migrațiile bazei de date", ExitCodes.SqlInstallError,
        (context, _) =>
        {
            var summary = new MigrationRunner(context.Log).RunMigrations(RequireMigrator(session).ConnectionString, session.LoadScripts());
            return Task.FromResult(StepResult.Done($"{summary.ScriptsExecuted} scripturi noi din {session.LoadScripts().Migrations.Count}"));
        });

    public static PlannedStep I10_Programmability(InstallSession session) => Step("I-10", "Actualizez obiectele programabile", ExitCodes.SqlInstallError,
        (context, _) =>
        {
            var summary = new MigrationRunner(context.Log).RunProgrammability(RequireMigrator(session).ConnectionString, session.LoadScripts());
            return Task.FromResult(StepResult.Done($"{summary.ScriptsExecuted} obiecte"));
        });

    public static PlannedStep I11_Seed(InstallSession session) => Step("I-11", "Încarc datele livrate", ExitCodes.SqlInstallError,
        (context, _) =>
        {
            var summary = new MigrationRunner(context.Log).RunSeed(RequireMigrator(session).ConnectionString, session.LoadScripts());
            return Task.FromResult(StepResult.Done($"{summary.ScriptsExecuted} scripturi"));
        });

    public static PlannedStep I12_License(InstallSession session, ILicenseReader verifier) => Step("I-12", "Încarc licența", ExitCodes.InvalidLicense,
        async (_, ct) =>
        {
            var answers = session.RequireAnswers();
            var installer = new LicenseInstaller(verifier, session.RequireSql());
            var verified = installer.Verify(answers.LicenseFilePath,
                DateOnly.ParseExact(session.Manifest.ReleaseDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), session.Manifest.Version);
            var license = verified.Result;
            await using var connection = new SqlConnection(RequireMigrator(session).ConnectionString);
            await connection.OpenAsync(ct);
            await installer.SaveAsync(connection, verified, ct);
            return license.UnknownModules.Count > 0
                ? StepResult.Warning($"{license.Message} Module necunoscute: {string.Join(", ", license.UnknownModules)}.")
                : StepResult.Done(license.Message);
        });

    public static PlannedStep I13_Administrator(InstallSession session) => Step("I-13", "Creez utilizatorul Administrator", ExitCodes.SqlInstallError,
        async (_, ct) =>
        {
            var admin = session.RequireAnswers().Administrator;
            await using var connection = new SqlConnection(RequireMigrator(session).ConnectionString);
            await connection.OpenAsync(ct);
            session.Administrator = await new InitialAdminCreator(session.RequireSql()).CreateAsync(connection,
                new InitialAdminRequest(admin.UserName, admin.DisplayName, admin.Email, session.AdminPassword, session.Manifest.Version, Environment.MachineName), ct);
            return session.Administrator.Created
                ? StepResult.Done($"{admin.UserName} (parolă temporară, schimbare obligatorie, 2FA obligatoriu)")
                : StepResult.Skipped(InitialAdminCreator.SkippedMessage);
        });

    public static PlannedStep I14_DisableMigrator(InstallSession session) => Step("I-14", "Dezactivez loginul de migrare", ExitCodes.SqlInstallError,
        async (_, ct) =>
        {
            await Logins(session).DisableMigratorAsync(ct);
            session.Migrator = null;
            return StepResult.Done();
        });

    public static PlannedStep I21_AgentJobs(InstallSession session) => Step("I-21", "Creez joburile SQL Agent", ExitCodes.SqlInstallError,
        async (_, ct) =>
        {
            var changes = await new AgentJobInstaller(session.RequireSql()).EnsureAsync(BackupSettings(session.RequireAnswers()), ct);
            return StepResult.Done($"{changes.Count} joburi ({string.Join(", ", changes.Values.Distinct())})");
        });

    public static PlannedStep I22_FirstBackup(InstallSession session, TimeSpan? pollInterval = null) => new(new DelegateStep("I-22",
        "Fac primul backup complet", async (_, ct) =>
        {
            var job = AgentJobDefinitions.FullBackupJob(session.RequireAnswers().Sql.DatabaseName);
            var run = await new AgentJobRunner(session.RequireSql()).RunAndWaitAsync(job, ct, pollInterval);
            return run.Succeeded ? StepResult.Done(run.Message) : StepResult.Failed(run.Message, ExitCodes.SqlInstallError);
        }), ExitCodes.SqlInstallError, SkipWhenCompleted: true);

    /// <summary>Dezactivarea loginului de migrare la esec (finally), cand I-14 nu a rulat.</summary>
    public static async Task DisableMigratorIfEnabledAsync(InstallSession session, CancellationToken ct)
    {
        if (session.Migrator is null || session.Sql is null)
        {
            return;
        }

        await Logins(session).DisableMigratorAsync(ct);
        session.Migrator = null;
    }

    public static BackupSettings BackupSettings(InstallAnswers answers) => new(answers.Sql.DatabaseName, answers.Backup.BackupRoot,
        answers.Backup.FullBackupTime, answers.Backup.LogBackupEveryMinutes, answers.Backup.RetentionDays, answers.Backup.IntegrityCheckDay,
        answers.Backup.IntegrityCheckTime);

    private static InstanceState NewState(InstallSession session)
    {
        var answers = session.RequireAnswers();
        var principals = SqlPrincipals.For(answers.Instance, answers.Sql.AppAuthentication, answers.Sql.AppWindowsAccount);
        return new InstanceState
        {
            Instance = answers.Instance,
            InstallId = session.InstallId,
            State = InstanceLifecycle.Installing,
            InstallRoot = answers.InstallRoot,
            DataRoot = answers.DataRoot,
            SiteName = $"ERP-{answers.Instance}",
            HostName = answers.Site.HostName,
            HttpsPort = answers.Site.HttpsPort,
            ServiceName = $"ErpWorker-{answers.Instance}",
            SqlServer = answers.Sql.Server,
            DatabaseName = answers.Sql.DatabaseName,
            AppAuthentication = answers.Sql.AppAuthentication.ToString(),
            AppLogin = principals.AppLogin,
            MigratorLogin = principals.MigratorLogin,
            BackupRoot = answers.Backup.BackupRoot,
        };
    }

    private static LoginManager Logins(InstallSession session)
    {
        var answers = session.RequireAnswers();
        return new LoginManager(session.RequireSql(), SqlPrincipals.For(answers.Instance, answers.Sql.AppAuthentication, answers.Sql.AppWindowsAccount));
    }

    private static SqlConnectionStringBuilder RequireMigrator(InstallSession session) =>
        session.Migrator ?? throw new InvalidOperationException("Loginul de migrare nu este activ (I-08).");

    private static Task<StepResult> Done(string message) => Task.FromResult(StepResult.Done(message));

    private static PlannedStep Step(string id, string title, int failureExitCode, Func<SetupContext, CancellationToken, Task<StepResult>> execute) =>
        new(new DelegateStep(id, title, execute), failureExitCode);
}
