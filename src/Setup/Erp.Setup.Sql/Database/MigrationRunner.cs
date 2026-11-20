using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.SqlServer;
using Serilog;

namespace Erp.Setup.Sql.Database;

/// <summary>
/// Pasul I-12 si U-06 (08, 5.9): migratiile cu jurnal in core.SchemaVersion, apoi obiectele programabile si datele livrate,
/// rulate toate la fiecare instalare/actualizare (MIG-04). Fiecare script aplicat al carui continut s-a schimbat primeste
/// un rand in core.ScriptVersion, cu versiunea din manifest (ScriptVersions). Ruleaza cu contul operatorului.
/// </summary>
public sealed class MigrationRunner(ILogger log, ScriptVersions? versions = null)
{
    private readonly ScriptVersions versions = versions ?? ScriptVersions.Development;

    public const string JournalSchema = "core";
    public const string JournalTable = "SchemaVersion";

    public MigrationSummary RunMigrations(string migratorConnectionString, KitScripts scripts) =>
        Run("Migrații", DeployChanges.To
            .SqlDatabase(migratorConnectionString)
            .WithScripts(scripts.Migrations)
            .JournalTo((connection, upgradeLog) => new VersionedMigrationJournal(
                new SqlTableJournal(connection, upgradeLog, JournalSchema, JournalTable),
                new ScriptVersionRecorder(ScriptKind.Migration, versions)))
            .WithTransactionPerScript()
            .WithExecutionTimeout(TimeSpan.FromMinutes(30))
            .WithVariablesDisabled()
            .LogTo(new SerilogUpgradeLog(log))
            .Build());

    public MigrationSummary RunProgrammability(string migratorConnectionString, KitScripts scripts) =>
        RunProgrammability(migratorConnectionString, scripts.Programmability);

    /// <summary>Doar obiectele date, in ordinea primita (baza de dezvoltare: fisierele schimbate de la ultima sincronizare).</summary>
    public MigrationSummary RunProgrammability(string migratorConnectionString, IEnumerable<SqlScript> scripts) =>
        RunAlways("Obiecte programabile", ScriptKind.Programmability, migratorConnectionString, scripts);

    public MigrationSummary RunSeed(string migratorConnectionString, KitScripts scripts) =>
        RunSeed(migratorConnectionString, scripts.Seed);

    /// <summary>Doar scripturile de date livrate date, in ordinea primita (baza de dezvoltare).</summary>
    public MigrationSummary RunSeed(string migratorConnectionString, IEnumerable<SqlScript> scripts) =>
        RunAlways("Date livrate", ScriptKind.Seed, migratorConnectionString, scripts);

    private MigrationSummary RunAlways(string what, ScriptKind kind, string migratorConnectionString, IEnumerable<SqlScript> scripts) =>
        Run(what, DeployChanges.To
            .SqlDatabase(migratorConnectionString)
            .WithScripts(scripts)
            .JournalTo(new ScriptVersionJournal(new ScriptVersionRecorder(kind, versions)))
            .WithTransactionPerScript()
            .WithExecutionTimeout(TimeSpan.FromMinutes(10))
            .WithVariablesDisabled()
            .LogTo(new SerilogUpgradeLog(log))
            .Build());

    private MigrationSummary Run(string what, UpgradeEngine engine)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = engine.PerformUpgrade();
        var count = result.Scripts.Count();
        if (!result.Successful)
        {
            // Mesajul contine numele scriptului si eroarea SQL, fara date din tabele (8.5).
            var script = result.ErrorScript?.Name ?? "(necunoscut)";
            log.Error("{What}: scriptul {Script} a eșuat: {Error}", what, script, result.Error?.Message);
            throw new SqlSetupException($"{what}: scriptul {script} a eșuat: {result.Error?.Message}", inner: result.Error);
        }

        log.Information("{What}: {Count} scripturi rulate ({ElapsedMs} ms)", what, count, watch.ElapsedMilliseconds);
        return new MigrationSummary(count, watch.Elapsed);
    }
}

public sealed record MigrationSummary(int ScriptsExecuted, TimeSpan Duration);

/// <summary>Jurnalul DbUp in Serilog. DbUp jurnalizeaza numele scripturilor, nu continutul lor.</summary>
public sealed class SerilogUpgradeLog(ILogger log) : IUpgradeLog
{
    private const string Template = "DbUp: {Message}";

    public void LogTrace(string format, params object[] args) => log.Verbose(Template, Format(format, args));

    public void LogDebug(string format, params object[] args) => log.Debug(Template, Format(format, args));

    public void LogInformation(string format, params object[] args) => log.Information(Template, Format(format, args));

    public void LogWarning(string format, params object[] args) => log.Warning(Template, Format(format, args));

    public void LogError(string format, params object[] args) => log.Error(Template, Format(format, args));

    public void LogError(Exception ex, string format, params object[] args) => log.Error(ex, Template, Format(format, args));

    private static string Format(string format, object[] args) =>
        args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
}