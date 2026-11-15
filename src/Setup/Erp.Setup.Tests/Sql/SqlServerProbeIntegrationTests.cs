using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.State;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Probes;
using Erp.Setup.Tests.Checks;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Erp.Setup.Tests.Sql;

/// <summary>Containerul SQL comun (Erp.Testing.Sql), ca fixture de colectie al acestui assembly.</summary>
public sealed class SqlServerFixture : SqlServerTestServer, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(StartAsync());

    public ValueTask DisposeAsync() => StopAsync();
}

/// <summary>Un singur container SQL pentru toate clasele de integrare.</summary>
[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Criteriu", "08-IA-20")]
public sealed class SqlServerProbeIntegrationTests(SqlServerFixture sql)
{
    private const string CaseSensitive = RequirementsChecker.RequiredCollation;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ISqlServerProbe> ConnectAsSa() => await new SqlServerConnector().ConnectAsync(sql.Target("master"), sql.SaPassword, Ct);

    private async Task<RequirementsReport> RunChecker(string database, string login = "sa", string? password = null, InstanceState? existing = null)
    {
        using var licenses = new TestLicenses();
        var license = CheckFixtures.WriteTemp(licenses.Create(TestLicenses.Payload()));
        try
        {
            var target = CheckFixtures.Target(license) with
            {
                Sql = sql.Target(database, login),
                DataFilePath = "/var/opt/mssql/data",
                LogFilePath = "/var/opt/mssql/data",
                BackupRoot = "/var/opt/mssql/data/erp-backup-check",
            };
            var checker = new RequirementsChecker(new FakeWindowsProbe(), new SqlServerConnector(), new FakeNetwork(),
                new LicenseVerifier(licenses.PublicKeys, TimeProvider.System), TimeProvider.System);
            return await checker.RunAsync(target, CheckFixtures.Manifest, existing, password ?? sql.SaPassword, Ct);
        }
        finally
        {
            File.Delete(license);
        }
    }

    private static CheckResult Result(RequirementsReport report, string id) => Assert.Single(report.Results, r => r.Id == id);

    [Fact]
    public async Task Server_info_is_read_from_a_real_server()
    {
        await using var probe = await ConnectAsSa();

        var info = await probe.GetServerInfoAsync(Ct);

        Assert.True(info.MajorVersion >= 16);
        Assert.Equal("SQL_Latin1_General_CP1_CI_AS", info.Collation);
        Assert.True(info.IsSysadmin);
        Assert.True((DateTime.UtcNow - info.UtcNow).Duration() < TimeSpan.FromMinutes(2));
        Assert.False(string.IsNullOrEmpty(info.DefaultDataPath));
    }

    [Fact]
    public async Task Wrong_password_fails_without_revealing_it()
    {
        const string password = "Gresit#Parola_123";

        var ex = await Assert.ThrowsAsync<SqlConnectFailedException>(() => new SqlServerConnector().ConnectAsync(sql.Target("master"), password, Ct));

        Assert.Contains("18456", ex.Message);
        Assert.DoesNotContain(password, ex.Message);
    }

    [Fact]
    public async Task Checker_against_a_real_server_reports_sql_checks()
    {
        var report = await RunChecker("ERP_IT_NEW");

        Assert.Equal(CheckStatus.Passed, Result(report, "C-09").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-10").Status);
        Assert.Equal(CheckStatus.Warning, Result(report, "C-11").Status); // imaginea ruleaza Developer
        Assert.Equal(CheckStatus.Passed, Result(report, "C-12").Status);
        Assert.Equal(CheckStatus.Warning, Result(report, "C-14").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-15").Status);
        Assert.Equal(CheckStatus.NotApplicable, Result(report, "C-16").Status);
        Assert.Equal(CheckStatus.Warning, Result(report, "C-17").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-19").Status);
    }

    [Fact]
    public async Task Login_without_server_rights_fails_C12_and_C13()
    {
        const string login = "erp_it_norights";
        const string password = "Fara#Drepturi_2026x";
        await sql.ExecuteAsync($"IF SUSER_ID(N'{login}') IS NULL CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;");

        var report = await RunChecker("ERP_IT_NEW", login, password);

        Assert.Equal(CheckStatus.Passed, Result(report, "C-09").Status);
        Assert.Equal(CheckStatus.Failed, Result(report, "C-12").Status);
        Assert.Equal(CheckStatus.Failed, Result(report, "C-13").Status);
        Assert.Equal(ExitCodes.RequirementsNotMet, report.ExitCode);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-03")]
    public async Task Existing_database_with_data_is_refused_with_code_31()
    {
        const string database = "ERP_IT_DATA";
        await sql.ExecuteAsync($"IF DB_ID(N'{database}') IS NULL CREATE DATABASE {SqlText.QuoteName(database)} COLLATE {CaseSensitive};");
        await sql.ExecuteAsync("IF OBJECT_ID(N'dbo.Existing') IS NULL CREATE TABLE dbo.Existing (Id int NOT NULL);", database);

        var report = await RunChecker(database);

        Assert.Equal(CheckStatus.Failed, Result(report, "C-15").Status);
        Assert.Equal(ExitCodes.DatabaseExistsNotEmpty, report.ExitCode);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-03")]
    public async Task Existing_empty_database_with_wrong_collation_is_refused_with_code_32()
    {
        const string database = "ERP_IT_CI";
        await sql.ExecuteAsync($"IF DB_ID(N'{database}') IS NULL CREATE DATABASE {SqlText.QuoteName(database)} COLLATE SQL_Latin1_General_CP1_CI_AS;");

        var report = await RunChecker(database);

        Assert.Equal(CheckStatus.Warning, Result(report, "C-15").Status);
        Assert.Equal(CheckStatus.Failed, Result(report, "C-16").Status);
        Assert.Equal(ExitCodes.WrongDatabaseCollation, report.ExitCode);
    }

    [Fact]
    public async Task Existing_empty_case_sensitive_database_is_accepted_with_a_warning()
    {
        const string database = "ERP_IT_EMPTY";
        await sql.ExecuteAsync($"IF DB_ID(N'{database}') IS NULL CREATE DATABASE {SqlText.QuoteName(database)} COLLATE {CaseSensitive};");

        var report = await RunChecker(database);

        Assert.Equal(CheckStatus.Warning, Result(report, "C-15").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-16").Status);
    }

    [Fact]
    public async Task Database_created_by_the_same_installation_is_accepted_on_resume()
    {
        const string database = "ERP_IT_RESUME";
        var installId = Guid.NewGuid();
        await sql.ExecuteAsync($"IF DB_ID(N'{database}') IS NULL CREATE DATABASE {SqlText.QuoteName(database)} COLLATE {CaseSensitive};");
        await sql.ExecuteAsync($"""
            EXEC sys.sp_addextendedproperty @name = N'ErpInstallId', @value = N'{installId}';
            CREATE TABLE dbo.Partial (Id int NOT NULL);
            """, database);
        var existing = new InstanceState { Instance = "PROD", InstallId = installId, State = InstanceLifecycle.Installing, InstallRoot = "x", DataRoot = "y" };

        var report = await RunChecker(database, existing: existing);

        Assert.Equal(CheckStatus.Passed, Result(report, "C-15").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-16").Status);
    }

    [Fact]
    public async Task Directory_states_and_creation()
    {
        await using var probe = await ConnectAsSa();
        var created = $"/var/opt/mssql/data/erp-it-{Guid.NewGuid():N}";

        Assert.Equal(SqlDirectoryState.Exists, await probe.GetDirectoryStateAsync("/var/opt/mssql/data", Ct));
        Assert.Equal(SqlDirectoryState.WillBeCreated, await probe.GetDirectoryStateAsync(created + "/FULL", Ct));
        // SQL Server pe Linux mapeaza orice cale X:\ pe sistemul lui de fisiere, deci o cale inaccesibila nu se poate
        // reproduce in container; cazul Unreachable e acoperit de RequirementsCheckerTests.Unreachable_sql_folder_fails_C17.

        await probe.CreateDirectoryAsync(created + "/FULL", Ct);

        Assert.Equal(SqlDirectoryState.Exists, await probe.GetDirectoryStateAsync(created + "/FULL", Ct));
    }

    [Fact]
    public async Task Database_size_and_free_space_queries_run()
    {
        await using var probe = await ConnectAsSa();

        var master = await probe.GetDatabaseAsync("master", Ct);
        var free = await probe.GetFreeSpaceBytesAsync("/var/opt/mssql/data", Ct);

        Assert.True(master!.SizeBytes > 0);
        Assert.True(free is null or > 0);
        Assert.Null(await probe.GetDatabaseAsync("ERP_IT_MISSING", Ct));
    }
}
