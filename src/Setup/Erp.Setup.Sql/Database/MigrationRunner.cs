using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Helpers;
using Serilog;

namespace Erp.Setup.Sql.Database;

/// <summary>
/// Pasii I-09…I-11 si U-10 (8.5, 8.6): migratiile cu jurnal in core.SchemaVersion, apoi obiectele programabile si datele
/// livrate rulate la fiecare instalare/actualizare (NullJournal). Ruleaza cu loginul de migrare.
/// </summary>
public sealed class MigrationRunner(ILogger log)
{
    public const string JournalSchema = "core";
    public const string JournalTable = "SchemaVersion";

    public MigrationSummary RunMigrations(string migratorConnectionString, KitScripts scripts) =>
        Run("Migrații", DeployChanges.To
            .SqlDatabase(migratorConnectionString)
            .WithScripts(scripts.Migrations)
            .JournalToSqlTable(JournalSchema, JournalTable)
            .WithTransactionPerScript()
            .WithExecutionTimeout(TimeSpan.FromMinutes(30))
            .WithVariablesDisabled()
            .LogTo(new SerilogUpgradeLog(log))
            .Build());

    public MigrationSummary RunProgrammability(string migratorConnectionString, KitScripts scripts) =>
        RunAlways("Obiecte programabile", migratorConnectionString, scripts.Programmability);

    public MigrationSummary RunSeed(string migratorConnectionString, KitScripts scripts) =>
        RunAlways("Date livrate", migratorConnectionString, scripts.Seed);

    private MigrationSummary RunAlways(string what, string migratorConnectionString, IEnumerable<SqlScript> scripts) =>
        Run(what, DeployChanges.To
            .SqlDatabase(migratorConnectionString)
            .WithScripts(scripts)
            .JournalTo(new NullJournal())
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