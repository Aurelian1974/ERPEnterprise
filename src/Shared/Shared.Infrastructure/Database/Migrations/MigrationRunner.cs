using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Shared.Infrastructure.Database.Migrations;

public static class MigrationRunner
{
    public static void Run(string connectionString, Assembly[] assemblies, ILogger logger)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssemblies(assemblies, IsVersionedScript)
            .WithTransaction()
            .LogTo(new DbUpLogger(logger))
            .Build();

        DatabaseUpgradeResult result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            logger.LogError(result.Error, "Database migration failed");
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        // Always-run scripts (stored procedures, views — idempotent)
        var alwaysRunUpgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssemblies(assemblies, IsAlwaysRunScript)
            .JournalTo(new NullJournal())
            .WithTransaction()
            .LogTo(new DbUpLogger(logger))
            .Build();

        DatabaseUpgradeResult alwaysRunResult = alwaysRunUpgrader.PerformUpgrade();

        if (!alwaysRunResult.Successful)
        {
            logger.LogError(alwaysRunResult.Error, "Always-run scripts failed");
            throw new InvalidOperationException("Always-run scripts failed.", alwaysRunResult.Error);
        }
    }

    private static bool IsVersionedScript(string name) =>
        name.Contains(".Migrations.") && name.EndsWith(".sql");

    private static bool IsAlwaysRunScript(string name) =>
        name.Contains(".StoredProcedures.") && name.EndsWith(".sql");
}

internal sealed class DbUpLogger : DbUp.Engine.Output.IUpgradeLog
{
    private readonly ILogger _logger;

    public DbUpLogger(ILogger logger) => _logger = logger;

    public void LogTrace(string format, params object[] args) =>
        _logger.LogTrace(format, args);

    public void LogDebug(string format, params object[] args) =>
        _logger.LogDebug(format, args);

    public void LogInformation(string format, params object[] args) =>
        _logger.LogInformation(format, args);

    public void LogWarning(string format, params object[] args) =>
        _logger.LogWarning(format, args);

    public void LogError(string format, params object[] args) =>
        _logger.LogError(format, args);

    public void LogError(Exception ex, string format, params object[] args) =>
        _logger.LogError(ex, format, args);
}

internal sealed class NullJournal : DbUp.Engine.IJournal
{
    public string[] GetExecutedScripts() => [];

    public void StoreExecutedScript(DbUp.Engine.SqlScript script, Func<System.Data.IDbCommand> dbCommandFactory)
    {
    }

    public void EnsureTableExistsAndIsLatestVersion(Func<System.Data.IDbCommand> dbCommandFactory)
    {
    }
}
