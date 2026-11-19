using Erp.Setup.Sql;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Logins;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>T7 pe SQL Server real: baza case sensitive pe un server SQL_Latin1_General_CP1_CI_AS (TST-09, IA-19).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Criteriu", "08-IA-19")]
public sealed class MigrationRunnerIntegrationTests(SqlServerFixture sql)
{
    private readonly CapturingSink logSink = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ILogger Log => new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(logSink).CreateLogger();

    private SqlOperator Operator() => sql.Operator(Log);

    private async Task<string> NewDatabase(string name)
    {
        var op = Operator();
        await new DatabaseCreator(op).CreateAsync(new DatabaseCreateOptions(name, "IT", Guid.NewGuid(), null, null, 256, 256), Ct);
        await new BootstrapRunner(op).RunAsync(name, Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap"), Ct);
        return name;
    }

    private async Task<(MigrationSummary Migrations, MigrationSummary Programmability, MigrationSummary Seed)> Deploy(string database, KitScripts scripts, string instance)
    {
        await Task.CompletedTask;
        var runner = new MigrationRunner(Log);
        var connectionString = Operator().ConnectionString(database);
        return (runner.RunMigrations(connectionString, scripts), runner.RunProgrammability(connectionString, scripts), runner.RunSeed(connectionString, scripts));
    }

    private async Task<T?> Scalar<T>(string database, string query)
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        return await SqlOperator.ScalarAsync<T>(connection, query, Ct);
    }

    [Fact]
    public async Task Repository_migrations_install_ids_and_hangfire_and_a_second_run_has_no_effect()
    {
        var database = await NewDatabase("ERP_IT_REPO");
        using var kit = TempKitDatabase.FromRepository();
        var scripts = KitScripts.Load(kit.Root);

        var first = await Deploy(database, scripts, "ITREPO");
        var second = await Deploy(database, scripts, "ITREPO");

        Assert.Equal(scripts.Migrations.Count, first.Migrations.ScriptsExecuted);
        Assert.Equal(0, second.Migrations.ScriptsExecuted);
        Assert.Equal(scripts.Migrations.Count, await Scalar<int>(database, "SELECT COUNT(*) FROM core.SchemaVersion;"));
        Assert.Equal(scripts.LastMigration, await Scalar<string>(database, "SELECT TOP (1) ScriptName FROM core.SchemaVersion ORDER BY Id DESC;"));
        Assert.Equal(9, await Scalar<int>(database, "SELECT [Version] FROM hangfire.[Schema];"));
        Assert.NotNull(await Scalar<int?>(database, "SELECT OBJECT_ID(N'ids.UserAccount');"));
        Assert.NotNull(await Scalar<int?>(database, "SELECT OBJECT_ID(N'hangfire.Job');"));
        Assert.Equal(0, await Scalar<int>(database, "SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0 AND SCHEMA_NAME(schema_id) = N'dbo';"));
    }

    [Fact]
    public async Task User_name_check_is_case_sensitive_on_the_erp_database()
    {
        var database = await NewDatabase("ERP_IT_CS");
        using var kit = TempKitDatabase.FromRepository();
        await Deploy(database, KitScripts.Load(kit.Root), "ITCS");

        const string insert = """
            INSERT INTO ids.UserAccount (SubjectId, UserName, NormalizedUserName, SecurityStamp, ConcurrencyStamp, CreatedBy)
            VALUES (NEWID(), N'{0}', UPPER(N'{0}'), N's', N'c', 1);
            """;
        await sql.ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, "administrator"), database);

        var duplicate = await Assert.ThrowsAsync<SqlException>(() =>
            sql.ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, "Administrator"), database));
        Assert.Equal(2627, duplicate.Number); // NormalizedUserName unic: Administrator = administrator (9.3)

        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            sql.ExecuteAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, insert, "Ion.Pop"), database));
        Assert.Equal(547, ex.Number); // CK_UserAccount_UserNameLower pe baza case sensitive (SQL-24)
    }

    [Fact]
    public async Task Migrations_programmability_and_seed_run_in_order_and_rerun_without_duplicates()
    {
        var database = await NewDatabase("ERP_IT_FLOW");
        using var kit = new TempKitDatabase()
            .Add("migrations/core/2026.11.001_Create_Thing.sql", """
                CREATE TABLE core.Thing (Code varchar(20) NOT NULL CONSTRAINT PK_Thing PRIMARY KEY, Name nvarchar(100) NOT NULL);
                """)
            .Add("migrations/sales/2026.11.001_Create_Order.sql", """
                CREATE TABLE sales.OrderLine (Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderLine PRIMARY KEY,
                    ThingCode varchar(20) NOT NULL CONSTRAINT FK_OrderLine_Thing REFERENCES core.Thing (Code));
                """)
            .Add("programmability/core/Functions/core.fn_Upper.sql", """
                CREATE OR ALTER FUNCTION core.fn_Upper (@Value nvarchar(100)) RETURNS nvarchar(100) AS BEGIN RETURN UPPER(@Value); END;
                """)
            .Add("programmability/core/Views/core.v_Thing.sql", """
                CREATE OR ALTER VIEW core.v_Thing AS SELECT t.Code, core.fn_Upper(t.Name) AS UpperName FROM core.Thing AS t;
                GO
                GRANT SELECT ON core.v_Thing TO role_sales;
                """)
            .Add("programmability/core/Procedures/core.usp_Thing_Find.sql", """
                CREATE OR ALTER PROCEDURE core.usp_Thing_Find @Name nvarchar(100)
                AS
                BEGIN
                    SET NOCOUNT ON;
                    CREATE TABLE #Names (Name nvarchar(100) COLLATE DATABASE_DEFAULT NOT NULL);
                    INSERT INTO #Names (Name) VALUES (@Name);
                    SELECT v.Code FROM core.v_Thing AS v JOIN core.Thing AS t ON t.Code = v.Code JOIN #Names AS n ON n.Name = t.Name;
                END;
                """)
            .Add("seed/core/01_Things.sql", """
                MERGE core.Thing AS target
                USING (VALUES ('RON', N'Leu românesc'), ('EUR', N'Euro')) AS source (Code, Name)
                ON target.Code = source.Code
                WHEN MATCHED THEN UPDATE SET Name = source.Name
                WHEN NOT MATCHED THEN INSERT (Code, Name) VALUES (source.Code, source.Name);
                """);
        var scripts = KitScripts.Load(kit.Root);

        var first = await Deploy(database, scripts, "ITFLOW");
        var second = await Deploy(database, scripts, "ITFLOW");

        Assert.Equal((2, 3, 1), (first.Migrations.ScriptsExecuted, first.Programmability.ScriptsExecuted, first.Seed.ScriptsExecuted));
        Assert.Equal((0, 3, 1), (second.Migrations.ScriptsExecuted, second.Programmability.ScriptsExecuted, second.Seed.ScriptsExecuted));
        Assert.Equal(2, await Scalar<int>(database, "SELECT COUNT(*) FROM core.Thing;"));
        Assert.Equal("LEU ROMÂNESC", await Scalar<string>(database, "SELECT UpperName FROM core.v_Thing WHERE Code = 'RON';"));
        Assert.Equal(["core/2026.11.001_Create_Thing.sql", "sales/2026.11.001_Create_Order.sql"],
            await ScalarList(database, "SELECT ScriptName FROM core.SchemaVersion ORDER BY Id;"));

        // Tabela temporara cu COLLATE DATABASE_DEFAULT (SQL-23) pe un server cu alt collation decat baza.
        Assert.Equal("RON", await Scalar<string>(database, "EXEC core.usp_Thing_Find @Name = N'Leu românesc';"));
    }

    [Fact]
    public async Task Failing_migration_is_reported_by_name_and_rolled_back()
    {
        var database = await NewDatabase("ERP_IT_FAIL");
        using var kit = new TempKitDatabase()
            .Add("migrations/core/2026.11.001_Good.sql", "CREATE TABLE core.Good (Id int NOT NULL);")
            .Add("migrations/core/2026.11.002_Bad.sql", "CREATE TABLE core.Partial (Id int NOT NULL);\nGO\nSELECT * FROM core.Missing;");

        var ex = await Assert.ThrowsAsync<SqlSetupException>(() => Deploy(database, KitScripts.Load(kit.Root), "ITFAIL"));

        Assert.Contains("core/2026.11.002_Bad.sql", ex.Message);
        Assert.Equal(["core/2026.11.001_Good.sql"], await ScalarList(database, "SELECT ScriptName FROM core.SchemaVersion ORDER BY Id;"));
        Assert.Null(await Scalar<int?>(database, "SELECT OBJECT_ID(N'core.Partial');"));
        Assert.True(await Scalar<bool>("master", "SELECT is_disabled FROM sys.server_principals WHERE name = N'erp_itfail_mig';"));
    }

    private async Task<List<string>> ScalarList(string database, string query)
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var values = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
