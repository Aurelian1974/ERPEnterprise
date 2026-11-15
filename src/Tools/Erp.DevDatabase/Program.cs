using System.Text;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Probes;
using Microsoft.Data.SqlClient;
using Serilog;

// Baza de dezvoltare, fara installer: aceiasi pasi SQL ca la instalare (I-06, I-07, I-09…I-11, optional I-13), cu contul Windows
// al dezvoltatorului. Doar pentru dezvoltare: nu creeaza loginurile _app/_mig, joburile SQL Agent si nu incarca licenta.
// Pornire: build/dev-database.ps1 (aseaza scripturile din depozit ca in kit, apoi ruleaza acest program).
Console.OutputEncoding = Encoding.UTF8;
var options = DevOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine("Utilizare: Erp.DevDatabase --server <server> --database <bază> --scripts <folder database> [--instance DEV] [--create-admin]");
    return 3;
}

using var log = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().CreateLogger();
var ct = CancellationToken.None;
var target = new SqlTarget(options.Server, options.Database, SqlAuthentication.Windows, null, SqlEncrypt.Mandatory, true, options.Instance);
var sql = new SqlOperator(target, null, log);

try
{
    var installId = await ReadInstallIdAsync(target, options.Database, ct) ?? Guid.NewGuid();
    var outcome = await new DatabaseCreator(sql).CreateAsync(
        new DatabaseCreateOptions(options.Database, options.Instance, installId, null, null, 256, 256), ct);
    log.Information("Baza {Database}: {Outcome}", options.Database, outcome);

    var bootstrap = await new BootstrapRunner(sql).RunAsync(options.Database, Path.Combine(options.Scripts, "bootstrap"), ct);
    log.Information("Bootstrap: {Count} scripturi", bootstrap);

    var connection = SqlServerConnector.BuildOperatorConnectionString(target, null);
    connection.InitialCatalog = options.Database;
    var scripts = KitScripts.Load(options.Scripts);
    var runner = new MigrationRunner(log);
    log.Information("Migrații: {Count} noi", runner.RunMigrations(connection.ConnectionString, scripts).ScriptsExecuted);
    log.Information("Obiecte programabile: {Count}", runner.RunProgrammability(connection.ConnectionString, scripts).ScriptsExecuted);
    log.Information("Date livrate: {Count} scripturi", runner.RunSeed(connection.ConnectionString, scripts).ScriptsExecuted);

    if (options.CreateAdmin)
    {
        await using var database = new SqlConnection(connection.ConnectionString);
        await database.OpenAsync(ct);
        var admin = await new InitialAdminCreator(sql).CreateAsync(database,
            new InitialAdminRequest("administrator", "Administrator ERP", null, null, "dev", Environment.MachineName), ct);
        if (admin.Created)
        {
            // Afisata o singura data, doar pe ecran (R6); in jurnal nu ajunge.
            Console.WriteLine();
            Console.WriteLine($"Administrator: {admin.UserName}, parola temporară: {admin.TemporaryPassword}");
        }
    }

    log.Information("Baza de dezvoltare {Database} pe {Server} este gata.", options.Database, options.Server);
    return 0;
}
catch (Exception ex) when (ex is SqlException or SqlSetupException or InvalidOperationException or IOException)
{
    log.Error(ex, "Pregătirea bazei de dezvoltare a eșuat");
    return 30;
}

// Reluarea pe aceeasi baza: DatabaseCreator accepta doar baza creata cu acelasi ErpInstallId (8.1).
static async Task<Guid?> ReadInstallIdAsync(SqlTarget target, string database, CancellationToken ct)
{
    await using var master = new SqlConnection(SqlServerConnector.BuildOperatorConnectionString(target, null).ConnectionString);
    await master.OpenAsync(ct);
    await using var exists = new SqlCommand("SELECT DB_ID(@name);", master);
    exists.Parameters.AddWithValue("@name", database);
    if (await exists.ExecuteScalarAsync(ct) is DBNull or null)
    {
        return null;
    }

    master.ChangeDatabase(database);
    await using var read = new SqlCommand(
        "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstallId';", master);
    return await read.ExecuteScalarAsync(ct) is string value && Guid.TryParse(value, out var id) ? id : null;
}

internal sealed record DevOptions(string Server, string Database, string Scripts, string Instance, bool CreateAdmin)
{
    public static DevOptions? Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var server = Value("--server");
        var database = Value("--database");
        var scripts = Value("--scripts");
        return server is null || database is null || scripts is null
            ? null
            : new DevOptions(server, database, scripts, Value("--instance") ?? "DEV", args.Contains("--create-admin"));
    }
}
