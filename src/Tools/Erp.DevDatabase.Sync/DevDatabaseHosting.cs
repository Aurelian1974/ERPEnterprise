using Erp.Setup.Core.Checks;
using Erp.Setup.Sql;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Erp.DevDatabase.Sync;

/// <summary>
/// Sincronizarea pornita din Erp.Host in Development (07, „Mediul de dezvoltare”), din aceeasi configurare ca aplicatia:
/// Database:Server, Database:Name, Database:TrustServerCertificate, Erp:Instance, plus DevDatabase:AdminPassword din
/// user-secrets. Conexiunea e cea a dezvoltatorului (Windows authentication), ca la build/dev-database.ps1.
/// </summary>
public static class DevDatabaseHosting
{
    public const string AutoSyncKey = "DevDatabase:AutoSync";

    public static bool Enabled(IConfiguration configuration) =>
        string.Equals(configuration[AutoSyncKey], "true", StringComparison.OrdinalIgnoreCase);

    public static (DevDatabaseSync Sync, DevDatabaseWatcher Watcher) Create(IConfiguration configuration, string contentRoot)
    {
        var database = configuration.GetSection("Database");
        var server = database["Server"] ?? throw new InvalidOperationException("Database:Server lipsește din configurarea de dezvoltare.");
        var name = database["Name"] ?? throw new InvalidOperationException("Database:Name lipsește din configurarea de dezvoltare.");
        var instance = configuration["Erp:Instance"] ?? "DEV";
        var trust = string.Equals(database["TrustServerCertificate"], "true", StringComparison.OrdinalIgnoreCase);

        var log = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();
        var repoRoot = RepositoryScripts.FindRoot(contentRoot);
        var sql = new SqlOperator(new SqlTarget(server, name, instance, trust), log);
        var sync = new DevDatabaseSync(sql, new DevSyncOptions(name, instance, repoRoot, configuration["DevDatabase:AdminPassword"],
            Path.Combine(repoRoot, "build", "dev-sample-data.sql")));
        return (sync, new DevDatabaseWatcher(sync, repoRoot, log));
    }
}
