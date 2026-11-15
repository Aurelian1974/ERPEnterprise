using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Probes;
using Microsoft.Data.SqlClient;
using Serilog;
using Testcontainers.MsSql;

namespace Erp.Testing.Sql;

public static class TestPaths
{
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Erp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Rădăcina depozitului nu a fost găsită.");
    }

    public static string BootstrapDirectory() => Path.Combine(RepositoryRoot(), "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap");
}

/// <summary>
/// SQL Server 2022 real in container, cu collation-ul implicit al serverului SQL_Latin1_General_CP1_CI_AS (TST-09).
/// Fiecare proiect de teste il foloseste printr-un fixture de colectie propriu (xUnit cere fixture-ul in assembly-ul testelor).
/// </summary>
public class SqlServerTestServer
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-latest";

    private readonly MsSqlContainer container = new MsSqlBuilder(Image)
        .WithEnvironment("MSSQL_AGENT_ENABLED", "true")
        .Build();

    public string Server { get; private set; } = string.Empty;

    public string SaPassword { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        await container.StartAsync();
        var builder = new SqlConnectionStringBuilder(container.GetConnectionString());
        Server = builder.DataSource;
        SaPassword = builder.Password;
    }

    public ValueTask StopAsync() => container.DisposeAsync();

    public SqlTarget Target(string database, string login = "sa") =>
        new(Server, database, SqlAuthentication.Sql, login, SqlEncrypt.Mandatory, TrustServerCertificate: true, "IT");

    /// <summary>Sirul de conexiune ca sa la o baza anume.</summary>
    public string ConnectionString(string database)
    {
        var builder = SqlServerConnector.BuildOperatorConnectionString(Target(database), SaPassword);
        builder.InitialCatalog = database;
        return builder.ConnectionString;
    }

    public async Task ExecuteAsync(string sql, string database = "master")
    {
        await using var connection = new SqlConnection(ConnectionString(database));
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Baza ERP completa, cu aceiasi pasi ca installer-ul (I-06, I-07, I-09…I-11): baza case sensitive, bootstrap,
    /// migratiile, obiectele programabile si datele livrate din depozit. Intoarce sirul de conexiune ca sa.
    /// </summary>
    public async Task<string> CreateErpDatabaseAsync(string database, CancellationToken ct)
    {
        var sql = new SqlOperator(Target("master"), SaPassword, new LoggerConfiguration().CreateLogger());
        await new DatabaseCreator(sql).CreateAsync(new DatabaseCreateOptions(database, "IT", Guid.NewGuid(), null, null, 256, 256), ct);
        await new BootstrapRunner(sql).RunAsync(database, TestPaths.BootstrapDirectory(), ct);
        using var kit = TempKitDatabase.FromRepository();
        var scripts = KitScripts.Load(kit.Root);
        var runner = new MigrationRunner(sql.Log);
        var connectionString = ConnectionString(database);
        runner.RunMigrations(connectionString, scripts);
        runner.RunProgrammability(connectionString, scripts);
        runner.RunSeed(connectionString, scripts);
        return connectionString;
    }
}
