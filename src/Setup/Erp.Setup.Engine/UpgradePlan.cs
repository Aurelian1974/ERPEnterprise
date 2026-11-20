using System.Globalization;
using Erp.Configuration;
using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Kit;
using Erp.Setup.Core.State;
using Erp.Setup.Core.Steps;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Agent;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Upgrade;

namespace Erp.Setup.Engine;

/// <summary>Starea unei actualizari, partajata intre pasii U-xx si R-xx; pasii terminati merg in upgrade-state.json (U-02).</summary>
public sealed class UpgradeSession : IStepJournal
{
    private UpgradeSession(InstallRequest request, InstanceState state, KitManifest manifest, string kitRoot, SqlOperator sql)
    {
        Request = request;
        Instance = state;
        Manifest = manifest;
        KitRoot = kitRoot;
        Sql = sql;
        Upgrade = UpgradeState.Read(UpgradeStatePath);
    }

    public InstallRequest Request { get; }

    public InstanceState Instance { get; private set; }

    public KitManifest Manifest { get; }

    public string KitRoot { get; }

    public string DatabaseRoot { get; init; } = string.Empty;

    public SqlOperator Sql { get; }

    public UpgradeState? Upgrade { get; private set; }

    /// <summary>Folderul de backup pe serverul SQL; backup-ul de actualizare merge in UPGRADE\ (10.2).</summary>
    public string? BackupFolder { get; init; }

    /// <summary>false = revenirea foloseste backup-ul (IA-10, „o data din backup”).</summary>
    public bool UseSnapshot { get; init; } = true;

    public UpgradeDatabaseInfo? Database { get; set; }

    public InstanceDataPaths Paths => InstanceDataPaths.For(Request.DataRoot, Request.Instance);

    public string StatePath => Paths.InstanceJson;

    public string UpgradeStatePath => UpgradeState.PathFor(Request.DataRoot, Request.Instance);

    public string FromVersion => Upgrade?.FromVersion ?? Instance.CurrentVersion ?? throw new InvalidOperationException("Versiunea instalată lipsește.");

    public string ToVersion => Manifest.Version;

    public static UpgradeSession Start(InstallRequest request, InstanceState state, KitManifest manifest, string kitRoot, SqlOperator sql,
        string? backupFolder, bool useSnapshot = true) =>
        new(request, state, manifest, kitRoot, sql)
        {
            DatabaseRoot = Path.Combine(kitRoot, "database"),
            BackupFolder = backupFolder ?? state.BackupFolder,
            UseSnapshot = useSnapshot,
        };

    internal static UpgradeSession ForTests(InstallRequest request, InstanceState state, KitManifest manifest, string databaseRoot, SqlOperator sql,
        string backupFolder, bool useSnapshot) =>
        new(request, state, manifest, Path.GetDirectoryName(databaseRoot) ?? databaseRoot, sql)
        {
            DatabaseRoot = databaseRoot,
            BackupFolder = backupFolder,
            UseSnapshot = useSnapshot,
        };

    /// <summary>Raspunsurile instalarii (install\answers.json, scris la I-18) si instance.json.</summary>
    public static (InstallRequest Request, InstanceState State) LoadInstallation(string dataRoot, string instance)
    {
        var paths = InstanceDataPaths.For(dataRoot, instance);
        var state = InstanceStateStore.Read(paths.InstanceJson)
            ?? throw new InvalidOperationException($"Instalarea {instance} nu există pe acest calculator.");
        var answers = AnswersValidator.Load(paths.Answers);
        if (!answers.IsValid)
        {
            throw new InvalidOperationException($"{paths.Answers} lipsește sau e invalid; folosiți „Repară”.");
        }

        return (answers.Request! with { DataRoot = dataRoot }, state);
    }

    public bool IsCompleted(string stepId) => Upgrade?.CompletedSteps.Contains(stepId) == true;

    public void MarkCompleted(string stepId)
    {
        if (Upgrade is not null && !Upgrade.CompletedSteps.Contains(stepId))
        {
            UpdateUpgrade(u => u with { CompletedSteps = [.. u.CompletedSteps, stepId] });
        }
    }

    public void BeginUpgrade(UpgradeState state)
    {
        Upgrade = state;
        UpgradeState.Write(UpgradeStatePath, state);
    }

    public void UpdateUpgrade(Func<UpgradeState, UpgradeState> change)
    {
        Upgrade = change(Upgrade ?? throw new InvalidOperationException("upgrade-state.json nu a fost creat (U-02)."));
        UpgradeState.Write(UpgradeStatePath, Upgrade);
    }

    public void UpdateInstance(Func<InstanceState, InstanceState> change)
    {
        Instance = change(Instance);
        InstanceStateStore.Write(StatePath, Instance);
    }

    public void EndUpgrade()
    {
        File.Delete(UpgradeStatePath);
        Upgrade = null;
    }
}

/// <summary>Rezultatul actualizarii (cod 0/1, 70 sau 71) si, la esec, pasul si motivul.</summary>
public sealed record UpgradeOutcome(int ExitCode, string? FailedStep, string? Reason, IReadOnlyList<string> ManualSteps);

/// <summary>
/// Actualizarea cu revenire automata (08, 10.2): U-01…U-10; la orice esec din U-03…U-09 ruleaza R-01…R-06. Daca si revenirea
/// esueaza, instalarea ramane in mentenanta (cod 71) cu pasii manuali.
/// </summary>
public sealed class UpgradeRunner(UpgradeSession session, IWindowsSetup windows, ILicenseReader licenses, TimeProvider time, IStepReporter reporter)
{
    public static readonly IReadOnlyList<(string Id, string Title)> UpgradeSteps =
    [
        ("U-01", "Verific versiunile, dreptul la actualizări și spațiul liber"),
        ("U-02", "Notez începutul actualizării"),
        ("U-03", "Trec aplicația în mentenanță"),
        ("U-04", "Fac backup-ul de siguranță și snapshot-ul bazei"),
        ("U-05", "Copiez programul nou și verific hash-urile"),
        ("U-06", "Actualizez baza de date"),
        ("U-07", "Regenerez configurarea și web.config"),
        ("U-08", "Comut site-ul și serviciul pe versiunea nouă"),
        ("U-09", "Pornesc aplicația și verific health-check-ul"),
        ("U-10", "Închei actualizarea"),
    ];

    public static readonly IReadOnlyList<(string Id, string Title)> RollbackSteps =
    [
        ("R-01", "Readuc site-ul și serviciul pe versiunea anterioară"),
        ("R-02", "Readuc baza de date"),
        ("R-03", "Readuc configurarea și accesul la bază"),
        ("R-04", "Pornesc versiunea anterioară și verific health-check-ul"),
        ("R-05", "Pornesc backup-ul complet și șterg snapshot-ul"),
        ("R-06", "Închei revenirea"),
    ];

    private readonly UpgradeDatabase database = new(session.Sql);

    public async Task<UpgradeOutcome> RunAsync(SetupContext context, CancellationToken ct)
    {
        var result = await new StepOrchestrator(reporter, session).RunAsync(Plan(), context, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            return new UpgradeOutcome(result.ExitCode, null, null, []);
        }

        var failed = result.Steps.Last(s => s.Result.Outcome == StepOutcome.Failed);
        if (session.Upgrade is null)
        {
            // U-01 sau U-02: nimic modificat.
            return new UpgradeOutcome(result.ExitCode, failed.StepId, failed.Result.Message, []);
        }

        context.Log.Warning("Actualizarea a eșuat la {Step}; revin la {Version}", failed.StepId, session.FromVersion);
        var rollback = await RollbackAsync(context, CancellationToken.None).ConfigureAwait(false);
        return rollback.ExitCode == ExitCodes.UpgradeFailedRolledBack
            ? rollback with { FailedStep = failed.StepId, Reason = failed.Result.Message }
            : rollback with { FailedStep = failed.StepId, Reason = $"{failed.Result.Message} Revenirea a eșuat: {rollback.Reason}" };
    }

    /// <summary>„Revino la versiunea anterioară” (rollback) dupa o actualizare intrerupta, sau revenirea automata.</summary>
    public async Task<UpgradeOutcome> RollbackAsync(SetupContext context, CancellationToken ct)
    {
        if (session.Upgrade is null)
        {
            return new UpgradeOutcome(ExitCodes.InvalidCommandLine, null, "Nu există o actualizare întreruptă.", []);
        }

        session.UpdateInstance(s => s with { State = InstanceLifecycle.RollingBack });
        var result = await new StepOrchestrator(reporter).RunAsync(RollbackPlan(), context, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            return new UpgradeOutcome(ExitCodes.UpgradeFailedRolledBack, null, null, []);
        }

        session.UpdateInstance(s => s with { State = InstanceLifecycle.Maintenance });
        var failed = result.Steps.Last(s => s.Result.Outcome == StepOutcome.Failed);
        return new UpgradeOutcome(ExitCodes.UpgradeFailedRollbackFailed, failed.StepId, failed.Result.Message, ManualSteps());
    }

    public IReadOnlyList<PlannedStep> Plan() =>
    [
        Step("U-01", ExitCodes.UnsupportedUpgradeVersion, (_, ct) => CheckAsync(ct)),
        Step("U-02", ExitCodes.IisServiceOrFileError, (_, _) =>
        {
            session.BeginUpgrade(new UpgradeState
            {
                FromVersion = session.Instance.CurrentVersion!,
                ToVersion = session.ToVersion,
                FromLastMigration = session.Database?.LastMigration ?? session.Instance.LastMigration,
                StartedAt = time.GetUtcNow(),
            });
            session.UpdateInstance(s => s with { State = InstanceLifecycle.Upgrading });
            return Task.FromResult(StepResult.Done($"{session.FromVersion} → {session.ToVersion}"));
        }),
        Step("U-03", ExitCodes.IisServiceOrFileError, async (_, ct) =>
        {
            windows.EnterMaintenance(session.Request, session.FromVersion);
            await database.SetRestrictedUserAsync(session.Request.Sql.Database, ct).ConfigureAwait(false);
            return StepResult.Done("pagina de mentenanță, serviciul oprit, baza în RESTRICTED_USER");
        }),
        Step("U-04", ExitCodes.SqlInstallError, (_, ct) => ProtectAsync(ct)),
        Step("U-05", ExitCodes.IisServiceOrFileError, (_, _) =>
        {
            var copied = windows.DeployBinaries(session.KitRoot, session.Manifest, session.Request, replaceExisting: true);
            return Task.FromResult(StepResult.Done($"{copied} fișiere în versions\\{session.ToVersion}"));
        }),
        Step("U-06", ExitCodes.SqlInstallError, (context, ct) => MigrateAsync(context, ct)),
        Step("U-07", ExitCodes.IisServiceOrFileError, (_, _) =>
        {
            windows.WriteConfiguration(session.KitRoot, session.Request, session.ToVersion, SettingsValues(session.ToVersion));
            return Task.FromResult(StepResult.Done(session.Paths.AppSettings));
        }),
        Step("U-08", ExitCodes.IisServiceOrFileError, (_, _) =>
        {
            windows.SwitchVersion(session.Request, session.ToVersion);
            return Task.FromResult(StepResult.Done($"versions\\{session.ToVersion}"));
        }),
        Step("U-09", ExitCodes.HealthCheckFailed, async (_, ct) =>
        {
            await database.SetMultiUserAsync(session.Request.Sql.Database, ct).ConfigureAwait(false);
            return await StartAsync(session.ToVersion, session.Manifest.LastMigration, ct).ConfigureAwait(false);
        }),
        Step("U-10", ExitCodes.IisServiceOrFileError, (_, ct) => FinishAsync(ct)),
    ];

    public IReadOnlyList<PlannedStep> RollbackPlan() =>
    [
        Rollback("R-01", (_, _) =>
        {
            windows.SwitchVersion(session.Request, session.FromVersion);
            return Task.FromResult(StepResult.Done($"versions\\{session.FromVersion}"));
        }),
        Rollback("R-02", (_, ct) => RestoreDatabaseAsync(ct)),
        Rollback("R-03", async (_, ct) =>
        {
            if (session.Upgrade!.ConfigBackupDirectory is { } folder && Directory.Exists(folder))
            {
                windows.RestoreConfiguration(session.Request, folder);
            }

            await database.SetMultiUserAsync(session.Request.Sql.Database, ct).ConfigureAwait(false);
            return StepResult.Done();
        }),
        Rollback("R-04", async (_, ct) =>
        {
            windows.RemoveAppOffline(session.Request, session.FromVersion);
            return await StartAsync(session.FromVersion, session.Upgrade!.FromLastMigration, ct).ConfigureAwait(false);
        }),
        Rollback("R-05", async (_, ct) =>
        {
            var restored = session.Upgrade!.DatabaseChanged;
            if (restored && session.Instance.BackupEdition == BackupEdition.Agent)
            {
                // Readucerea bazei (din snapshot sau din backup) intrerupe lantul backup-urilor de log.
                await new AgentJobRunner(session.Sql).StartAsync(AgentJobDefinitions.FullBackupJob(session.Request.Sql.Database), ct).ConfigureAwait(false);
            }

            await database.DropSnapshotAsync(session.Request.Sql.Database, ct).ConfigureAwait(false);
            return restored ? StepResult.Done("backup complet pornit") : StepResult.Skipped("baza nu a fost modificată");
        }),
        Rollback("R-06", (_, _) =>
        {
            windows.ClearMaintenanceFlag(session.Request);
            var reachedVersion = session.FromVersion;
            session.UpdateInstance(s => s with { State = InstanceLifecycle.Installed, CurrentVersion = reachedVersion, LastMigration = session.Upgrade!.FromLastMigration });
            session.EndUpgrade();
            return Task.FromResult(StepResult.Done($"instalarea funcționează pe {reachedVersion}"));
        }),
    ];

    private async Task<StepResult> CheckAsync(CancellationToken ct)
    {
        var installed = Version.Parse(session.Instance.CurrentVersion ?? "0.0.0");
        var kit = Version.Parse(session.ToVersion);
        if (kit <= installed)
        {
            return StepResult.Failed($"Kitul {kit} nu este mai nou decât versiunea instalată {installed}.", ExitCodes.UnsupportedUpgradeVersion);
        }

        if (installed < Version.Parse(session.Manifest.MinUpgradeFrom))
        {
            return StepResult.Failed($"Versiunea {installed} e prea veche pentru acest kit (minimum {session.Manifest.MinUpgradeFrom}); actualizați întâi la o versiune intermediară.",
                ExitCodes.UnsupportedUpgradeVersion);
        }

        var info = session.Database = await database.ReadAsync(session.Request.Sql.Database, ct).ConfigureAwait(false);
        if (info.CurrentLicenseFileBase64 is null || licenses.Read(Convert.FromBase64String(info.CurrentLicenseFileBase64)) is not { IsValid: true } license)
        {
            return StepResult.Failed("Licența din bază lipsește sau nu e validă.", ExitCodes.InvalidLicense);
        }

        var release = DateOnly.ParseExact(session.Manifest.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (LicensePolicy.CheckKitRelease(license.License!, release, session.ToVersion) is { } refused)
        {
            return StepResult.Failed(refused, ExitCodes.LicenseExcludesKit);
        }

        if (info.FreeBytesOnDataVolume is { } free && free < info.UsedBytes)
        {
            return StepResult.Failed($"Pe volumul fișierelor de date sunt liberi {free / 1048576} MB, sub dimensiunea bazei ({info.UsedBytes / 1048576} MB).",
                ExitCodes.RequirementsNotMet);
        }

        var estimate = info.EstimatedDuration is { } duration ? $"durata estimată {Math.Ceiling(duration.TotalMinutes)} minute" : "durata nu se poate estima";
        return StepResult.Done($"{session.Instance.CurrentVersion} → {session.ToVersion}, {estimate}");
    }

    private async Task<StepResult> ProtectAsync(CancellationToken ct)
    {
        var db = session.Request.Sql.Database;
        var folder = DatabaseScripts.CombinePath(session.BackupFolder ?? throw new InvalidOperationException("Folderul de backup nu e cunoscut."), "UPGRADE");
        var file = DatabaseScripts.CombinePath(folder, UpgradeDatabase.BackupFileName(db, session.FromVersion, session.ToVersion, time.GetUtcNow()));
        await database.CreateFolderAsync(folder, ct).ConfigureAwait(false);
        await database.BackupAsync(db, file, ct).ConfigureAwait(false);
        var config = Path.Combine(session.Paths.UpgradeDirectory, $"config-{session.FromVersion}");
        windows.BackupConfiguration(session.Request, config);
        session.UpdateUpgrade(u => u with { BackupFile = file, ConfigBackupDirectory = config });

        var snapshot = session.UseSnapshot && session.Database?.SnapshotPossible != false;
        if (snapshot)
        {
            await database.DropSnapshotAsync(db, ct).ConfigureAwait(false);
            await database.CreateSnapshotAsync(db, ct).ConfigureAwait(false);
            session.UpdateUpgrade(u => u with { SnapshotCreated = true });
        }

        return StepResult.Done(snapshot ? $"{file}, snapshot {UpgradeDatabase.SnapshotName(db)}" : $"{file} (fără snapshot)");
    }

    private Task<StepResult> MigrateAsync(SetupContext context, CancellationToken ct)
    {
        session.UpdateUpgrade(u => u with { DatabaseChanged = true });
        return MigrateCoreAsync(context, ct);
    }

    private async Task<StepResult> MigrateCoreAsync(SetupContext context, CancellationToken ct)
    {
        var db = session.Request.Sql.Database;
        await new BootstrapRunner(session.Sql).RunAsync(db, Path.Combine(session.DatabaseRoot, "bootstrap"), ct).ConfigureAwait(false);
        var scripts = KitScripts.Load(session.DatabaseRoot);
        var runner = new MigrationRunner(context.Log, session.Manifest.ScriptVersionsOf());
        var connection = session.Sql.ConnectionString(db);
        var migrations = runner.RunMigrations(connection, scripts);
        runner.RunProgrammability(connection, scripts);
        runner.RunSeed(connection, scripts);
        return StepResult.Done($"{migrations.ScriptsExecuted} migrații noi");
    }

    private async Task<StepResult> StartAsync(string version, string? lastMigration, CancellationToken ct)
    {
        windows.StartApplication(session.Request, version);
        var pinned = session.Instance.CertificateKind == CertificateKind.Internal ? session.Instance.CertificateThumbprint : null;
        var outcome = await windows.CheckHealthAsync(session.Request, version, lastMigration, pinned, ct).ConfigureAwait(false);
        return outcome.Succeeded
            ? StepResult.Done(outcome.Message)
            : StepResult.Failed($"Health-check eșuat: {outcome.Message}{Environment.NewLine}{string.Join(Environment.NewLine, windows.ComponentLogTail(session.Request))}",
                ExitCodes.HealthCheckFailed);
    }

    private async Task<StepResult> RestoreDatabaseAsync(CancellationToken ct)
    {
        var upgrade = session.Upgrade!;
        if (!upgrade.DatabaseChanged)
        {
            return StepResult.Skipped("baza nu a fost modificată");
        }

        var db = session.Request.Sql.Database;
        string source;
        if (upgrade.SnapshotCreated && await database.SnapshotExistsAsync(db, ct).ConfigureAwait(false))
        {
            await database.RestoreFromSnapshotAsync(db, ct).ConfigureAwait(false);
            source = "din snapshot";
        }
        else
        {
            await database.RestoreFromBackupAsync(db, upgrade.BackupFile ?? throw new InvalidOperationException("Backup-ul de siguranță lipsește."), ct)
                .ConfigureAwait(false);
            source = "din backup";
        }

        var info = await database.ReadAsync(db, ct).ConfigureAwait(false);
        return info.LastMigration == upgrade.FromLastMigration
            ? StepResult.Done($"baza readusă {source}, la {info.LastMigration}")
            : StepResult.Failed($"Baza readusă {source} are ultima migrare {info.LastMigration}, nu {upgrade.FromLastMigration}.");
    }

    private async Task<StepResult> FinishAsync(CancellationToken ct)
    {
        var db = session.Request.Sql.Database;
        windows.ClearMaintenanceFlag(session.Request);
        await database.DropSnapshotAsync(db, ct).ConfigureAwait(false);
        var from = session.FromVersion;
        var backups = new[] { session.Upgrade!.BackupFile }.OfType<string>().Concat(session.Instance.UpgradeBackups).ToList();
        foreach (var old in backups.Skip(3))
        {
            await database.DeleteFileAsync(old, ct).ConfigureAwait(false);
        }

        session.UpdateInstance(s => s with
        {
            State = InstanceLifecycle.Installed,
            CurrentVersion = session.ToVersion,
            PreviousVersions = [from, .. s.PreviousVersions.Where(v => v != from)],
            LastUpgradeAt = time.GetUtcNow(),
            LastMigration = session.Manifest.LastMigration ?? s.LastMigration,
            UpgradeBackups = [.. backups.Take(3)],
        });
        windows.RemoveAppOffline(session.Request, from);
        windows.PruneVersions(session.Request, [session.ToVersion, .. session.Instance.PreviousVersions.Take(2)]);
        session.EndUpgrade();
        return StepResult.Done($"instalarea {session.Request.Instance} e la {session.ToVersion}");
    }

    /// <summary>Pasii manuali (cod 71), cu valorile reale (10.2).</summary>
    private IReadOnlyList<string> ManualSteps()
    {
        var upgrade = session.Upgrade!;
        var db = session.Request.Sql.Database;
        return
        [
            upgrade.SnapshotCreated
                ? $"Readuceți baza din snapshot: ALTER DATABASE [{db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [{db}] FROM DATABASE_SNAPSHOT = N'{UpgradeDatabase.SnapshotName(db)}';"
                : $"Readuceți baza din backup: RESTORE DATABASE [{db}] FROM DISK = N'{upgrade.BackupFile}' WITH REPLACE, CHECKSUM, RECOVERY;",
            $"Site-ul {session.Request.SiteName} și serviciul {session.Request.ServiceName} trebuie să indice versions\\{upgrade.FromVersion}.",
            $"Rulați apoi „Repară” din kitul versiunii {upgrade.FromVersion}.",
        ];
    }

    private Core.Deployment.InstallationSettingsValues SettingsValues(string version) => new(session.Request.Instance, version,
        Core.Deployment.AppSettingsGenerator.PublicOrigin(session.Request.Site.HostName, session.Request.Site.HttpsPort), session.Paths.InstanceDirectory,
        session.Request.Sql.Server, session.Request.Sql.Database, session.Instance.TrustServerCertificate, session.Instance.DataProtectionCertificateThumbprint);

    private static PlannedStep Step(string id, int exitCode, Func<SetupContext, CancellationToken, Task<StepResult>> execute) =>
        new(new DelegateStep(id, UpgradeSteps.Single(s => s.Id == id).Title, execute), exitCode);

    private static PlannedStep Rollback(string id, Func<SetupContext, CancellationToken, Task<StepResult>> execute) =>
        new(new DelegateStep(id, RollbackSteps.Single(s => s.Id == id).Title, execute), ExitCodes.UpgradeFailedRollbackFailed);
}
