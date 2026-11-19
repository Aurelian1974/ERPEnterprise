using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Logins;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>T6 pe SQL Server real: baza case sensitive pe un server CI (TST-09), bootstrap, loginuri.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DatabaseSetupIntegrationTests(SqlServerFixture sql)
{
    private readonly CapturingSink logSink = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string BootstrapFolder => Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap");

    private SqlOperator Operator()
    {
        var log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(logSink).CreateLogger();
        return sql.Operator(log);
    }

    private static DatabaseCreateOptions Options(string name, Guid? installId = null) =>
        new(name, "IT", installId ?? Guid.NewGuid(), null, null, 256, 256);

    private async Task<T?> Scalar<T>(string query, string database = "master")
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        return await SqlOperator.ScalarAsync<T>(connection, query, Ct);
    }

    private static async Task<T?> ScalarAs<T>(SqlConnectionStringBuilder builder, string query)
    {
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(Ct);
        return await SqlOperator.ScalarAsync<T>(connection, query, Ct);
    }

    private async Task<string> CreateBootstrappedDatabase(string name)
    {
        var op = Operator();
        await new DatabaseCreator(op).CreateAsync(Options(name), Ct);
        await new BootstrapRunner(op).RunAsync(name, BootstrapFolder, Ct);
        return name;
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public async Task New_database_is_case_sensitive_with_rcsi_full_recovery_and_compatibility_160()
    {
        var installId = Guid.NewGuid();

        var outcome = await new DatabaseCreator(Operator()).CreateAsync(Options("ERP_IT_CREATE", installId), Ct);

        Assert.Equal(DatabaseCreateOutcome.Created, outcome);
        Assert.Equal(DatabaseScripts.Collation, await Scalar<string>("SELECT collation_name FROM sys.databases WHERE name = N'ERP_IT_CREATE';"));
        Assert.True(await Scalar<bool>("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'ERP_IT_CREATE';"));
        Assert.Equal("FULL", await Scalar<string>("SELECT recovery_model_desc FROM sys.databases WHERE name = N'ERP_IT_CREATE';"));
        Assert.Equal((byte)160, await Scalar<byte>("SELECT compatibility_level FROM sys.databases WHERE name = N'ERP_IT_CREATE';"));
        Assert.Equal(0, await Scalar<byte>("SELECT snapshot_isolation_state FROM sys.databases WHERE name = N'ERP_IT_CREATE';"));
        Assert.Equal("sa", await Scalar<string>("SELECT SUSER_SNAME(owner_sid) FROM sys.databases WHERE name = N'ERP_IT_CREATE';"));
        Assert.Equal(installId.ToString(), await Scalar<string>(
            "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstallId';", "ERP_IT_CREATE"));
        Assert.Equal("IT", await Scalar<string>(
            "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstance';", "ERP_IT_CREATE"));
    }

    [Fact]
    public async Task Creating_again_with_the_same_install_id_resumes_without_changes()
    {
        var installId = Guid.NewGuid();
        var creator = new DatabaseCreator(Operator());
        await creator.CreateAsync(Options("ERP_IT_RERUN", installId), Ct);

        var second = await creator.CreateAsync(Options("ERP_IT_RERUN", installId), Ct);

        Assert.Equal(DatabaseCreateOutcome.Resumed, second);
        Assert.Equal(1, await Scalar<int>("SELECT COUNT(*) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstallId';", "ERP_IT_RERUN"));
    }

    [Fact]
    public async Task Existing_empty_case_sensitive_database_is_adopted_and_configured()
    {
        await sql.ExecuteAsync($"CREATE DATABASE [ERP_IT_ADOPT] COLLATE {DatabaseScripts.Collation};");
        var installId = Guid.NewGuid();

        var outcome = await new DatabaseCreator(Operator()).CreateAsync(Options("ERP_IT_ADOPT", installId), Ct);

        Assert.Equal(DatabaseCreateOutcome.AdoptedEmpty, outcome);
        Assert.True(await Scalar<bool>("SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'ERP_IT_ADOPT';"));
        Assert.Equal(installId.ToString(), await Scalar<string>(
            "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class = 0 AND name = N'ErpInstallId';", "ERP_IT_ADOPT"));
    }

    [Theory]
    [Trait("Criteriu", "08-IA-03")]
    [InlineData("ERP_IT_WRONGCOLL", "SQL_Latin1_General_CP1_CI_AS", ExitCodes.WrongDatabaseCollation)]
    [InlineData("ERP_IT_HASDATA", DatabaseScripts.Collation, ExitCodes.DatabaseExistsNotEmpty)]
    public async Task Existing_database_is_refused_without_any_change(string name, string collation, int exitCode)
    {
        await sql.ExecuteAsync($"CREATE DATABASE [{name}] COLLATE {collation};");
        await sql.ExecuteAsync("CREATE TABLE dbo.ClientData (Id int NOT NULL); INSERT INTO dbo.ClientData (Id) VALUES (1);", name);

        var ex = await Assert.ThrowsAsync<SqlSetupException>(() => new DatabaseCreator(Operator()).CreateAsync(Options(name), Ct));

        Assert.Equal(exitCode, ex.ExitCode);
        Assert.Equal(collation, await Scalar<string>($"SELECT collation_name FROM sys.databases WHERE name = N'{name}';"));
        Assert.False(await Scalar<bool>($"SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = N'{name}';"));
        Assert.Equal(0, await Scalar<int>("SELECT COUNT(*) FROM sys.extended_properties WHERE class = 0;", name));
        Assert.Equal(1, await Scalar<int>("SELECT COUNT(*) FROM dbo.ClientData;", name));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-03")]
    public async Task Leftover_data_file_is_not_overwritten()
    {
        await sql.ExecuteAsync("""
            CREATE DATABASE [ERP_IT_OWNER]
            ON PRIMARY (NAME = N'owner_data', FILENAME = N'/var/opt/mssql/data/ERP_IT_FILES.mdf')
            LOG ON (NAME = N'owner_log', FILENAME = N'/var/opt/mssql/data/ERP_IT_OWNER_log.ldf');
            """);

        var ex = await Assert.ThrowsAsync<SqlSetupException>(() => new DatabaseCreator(Operator()).CreateAsync(Options("ERP_IT_FILES"), Ct));

        Assert.Equal(ExitCodes.SqlInstallError, ex.ExitCode);
        Assert.Contains("ERP_IT_FILES.mdf", ex.Message);
        Assert.Null(await Scalar<int?>("SELECT DB_ID(N'ERP_IT_FILES');"));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-19")]
    public async Task Bootstrap_creates_all_schemas_and_roles_and_is_idempotent()
    {
        var database = await CreateBootstrappedDatabase("ERP_IT_BOOT");

        await new BootstrapRunner(Operator()).RunAsync(database, BootstrapFolder, Ct);

        Assert.Equal(16, await Scalar<int>($"""
            SELECT COUNT(*) FROM sys.schemas AS s
            WHERE s.name IN ({string.Join(", ", ModuleOrder.Schemas.Select(s => $"N'{s}'"))});
            """, database));
        Assert.Equal(16, await Scalar<int>("SELECT COUNT(*) FROM sys.database_principals WHERE type = 'R' AND name LIKE N'role[_]%';", database));
        Assert.Equal(16 * 5, await Scalar<int>("""
            SELECT COUNT(*)
            FROM sys.database_permissions AS p
            JOIN sys.schemas AS s ON s.schema_id = p.major_id
            JOIN sys.database_principals AS r ON r.principal_id = p.grantee_principal_id
            WHERE p.class = 3 AND p.state = 'G' AND r.name = N'role_' + s.name
              AND p.permission_name IN (N'SELECT', N'INSERT', N'UPDATE', N'DELETE', N'EXECUTE');
            """, database));
        Assert.Equal(0, await Scalar<int>("SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped = 0 AND SCHEMA_NAME(schema_id) = N'dbo';", database));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-14")]
    public async Task App_identity_has_module_roles_but_no_db_owner_and_no_server_rights()
    {
        var database = await CreateBootstrappedDatabase("ERP_IT_APP");
        const string login = "erp_it_app";

        // Loginul Windows al aplicatiei (I-11) e inlocuit in container de un login SQL cu acelasi utilizator si aceleasi roluri;
        // a doua rulare (reluare, „Repară”) nu dubleaza nimic.
        var app = new SqlConnectionStringBuilder(await sql.CreateAppLoginAsync(database, login, Ct));
        await new LoginManager(Operator()).EnsureUsersAsync(database, [new AppPrincipal(login, "erp_app")], Ct);

        Assert.Equal(0, await ScalarAs<int>(app, "SELECT IS_SRVROLEMEMBER(N'sysadmin') + IS_SRVROLEMEMBER(N'dbcreator') + IS_SRVROLEMEMBER(N'securityadmin');"));
        Assert.Equal(0, await ScalarAs<int>(app, "SELECT IS_MEMBER(N'db_owner');"));
        Assert.Equal(1, await ScalarAs<int>(app, "SELECT IS_ROLEMEMBER(N'role_core');"));
        Assert.Equal(1, await ScalarAs<int>(app, "SELECT HAS_PERMS_BY_NAME(N'rpt', N'SCHEMA', N'EXECUTE');"));
        Assert.Equal(0, await ScalarAs<int>(app, "SELECT HAS_PERMS_BY_NAME(NULL, N'DATABASE', N'CREATE TABLE');"));
        Assert.Equal("core", await ScalarAs<string>(app, "SELECT default_schema_name FROM sys.database_principals WHERE name = N'erp_app';"));
        Assert.Equal(0, await Scalar<int>($"""
            SELECT COUNT(*) FROM sys.server_role_members AS m
            JOIN sys.server_principals AS p ON p.principal_id = m.member_principal_id
            WHERE p.name = N'{login}';
            """));
        var msdb = new SqlConnectionStringBuilder(app.ConnectionString) { InitialCatalog = "msdb" };
        Assert.True(await ScalarAs<int>(msdb, "SELECT COUNT(*) FROM dbo.backupset;") >= 0);
    }

    [Fact]
    public async Task Windows_logins_are_validated_before_any_sql_text_is_built()
    {
        Assert.Throws<SqlSetupException>(() => LoginScripts.CreateWindowsLogin("erp_app", "ERP_PROD"));
        Assert.Throws<SqlSetupException>(() => LoginScripts.CreateWindowsLogin("DOMENIU\\x]; DROP LOGIN sa; --", "ERP_PROD"));
        Assert.Contains("CREATE LOGIN [IIS APPPOOL\\ERP-PROD] FROM WINDOWS", LoginScripts.CreateWindowsLogin("IIS APPPOOL\\ERP-PROD", "ERP_PROD"));

        // Dezactivarea la „Dezinstalează”: loginul ramane, dezactivat.
        await sql.ExecuteAsync("IF SUSER_ID(N'erp_it_off') IS NULL CREATE LOGIN [erp_it_off] WITH PASSWORD = N'Off#Parola_2026x', CHECK_POLICY = OFF;");
        await new LoginManager(Operator()).DisableAsync(["erp_it_off"], Ct);
        Assert.True(await Scalar<bool>("SELECT is_disabled FROM sys.server_principals WHERE name = N'erp_it_off';"));
    }
}
