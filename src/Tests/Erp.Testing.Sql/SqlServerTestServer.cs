using Erp.Setup.Core.Checks;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Logins;
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
/// Optional, pentru masinile fara Docker si numai pentru testele de modul (nu Erp.Setup.Tests, care creeaza login-uri si joburi):
/// ERP_TEST_SQL_SERVER = un SQL Server existent, cu autentificare Windows; bazele de test (doar ERP_IT_*) se recreeaza la fiecare rulare.
/// </summary>
public class SqlServerTestServer
{
    public const string Image = "mcr.microsoft.com/mssql/server:2022-latest";
    public const string ExternalServerVariable = "ERP_TEST_SQL_SERVER";

    private readonly string? externalServer = Environment.GetEnvironmentVariable(ExternalServerVariable) is { Length: > 0 } server ? server : null;
    private MsSqlContainer? container;

    public string Server { get; private set; } = string.Empty;

    public string? SaPassword { get; private set; }

    public async Task StartAsync()
    {
        if (externalServer is not null)
        {
            Server = externalServer;
            return;
        }

        container = new MsSqlBuilder(Image).WithEnvironment("MSSQL_AGENT_ENABLED", "true").Build();
        await container.StartAsync();
        var builder = new SqlConnectionStringBuilder(container.GetConnectionString());
        Server = builder.DataSource;
        SaPassword = builder.Password;
    }

    public ValueTask StopAsync() => container?.DisposeAsync() ?? ValueTask.CompletedTask;

    /// <summary>Tinta operatorului; in container autentificarea e SQL (vezi <see cref="Credentials"/>), altfel Windows.</summary>
    public SqlTarget Target(string database) => new(Server, database, "IT", TrustServerCertificate: true);

    /// <summary>
    /// Installer-ul foloseste doar Windows authentication (08, D3); containerul Linux nu o are, deci testele inlocuiesc
    /// autentificarea cu un login SQL. Pe un server extern (ERP_TEST_SQL_SERVER) ramane Windows authentication.
    /// </summary>
    public Action<SqlConnectionStringBuilder>? Credentials(string login = "sa", string? password = null)
    {
        if (externalServer is not null)
        {
            return null;
        }

        var secret = password ?? SaPassword;
        return builder =>
        {
            builder.IntegratedSecurity = false;
            builder.UserID = login;
            builder.Password = secret;
        };
    }

    /// <summary>Operatorul installer-ului (implicit sa).</summary>
    public SqlOperator Operator(ILogger? log = null, string login = "sa", string? password = null) =>
        new(Target("master"), log ?? new LoggerConfiguration().CreateLogger(), Credentials(login, password));

    public SqlServerConnector Connector(string login = "sa", string? password = null) => new(Credentials(login, password));

    /// <summary>Sirul de conexiune ca sa la o baza anume.</summary>
    public string ConnectionString(string database) =>
        SqlServerConnector.BuildOperatorConnectionString(Target(database), database, Credentials()).ConnectionString;

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
        if (externalServer is not null)
        {
            await DropTestDatabaseAsync(database);
        }

        var sql = Operator();
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

    /// <summary>
    /// Echivalentul identitatii aplicatiei (08, 5.8, I-11) in containerul Linux, unde loginurile Windows nu exista: un login SQL cu
    /// acelasi utilizator si aceleasi roluri ca loginul Windows real (fara db_owner). Intoarce sirul de conexiune al aplicatiei.
    /// </summary>
    public async Task<string> CreateAppLoginAsync(string database, string login, CancellationToken ct)
    {
        var password = "It#" + Guid.NewGuid().ToString("N") + "aA1!";
        await ExecuteAsync($"""
            IF SUSER_ID(N'{login}') IS NULL CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;
            ELSE ALTER LOGIN [{login}] WITH PASSWORD = N'{password}';
            """);
        await new LoginManager(Operator()).EnsureUsersAsync(database, [new AppPrincipal(login, "erp_app")], ct);
        var builder = new SqlConnectionStringBuilder(ConnectionString(database))
        {
            IntegratedSecurity = false,
            UserID = login,
            Password = password,
            ApplicationName = "ERP-IT-App",
        };
        return builder.ConnectionString;
    }

    /// <summary>Pe un server existent (ERP_TEST_SQL_SERVER) se sterg doar bazele de test ERP_IT_*.</summary>
    public async Task DropTestDatabaseAsync(string database)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(database, "^ERP_IT_[A-Z0-9_]+$"))
        {
            throw new InvalidOperationException($"Se pot șterge doar bazele de test ERP_IT_*, nu {database}.");
        }

        await ExecuteAsync($"""
            IF DB_ID(N'{database}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{database}];
            END;
            """);
    }
}
