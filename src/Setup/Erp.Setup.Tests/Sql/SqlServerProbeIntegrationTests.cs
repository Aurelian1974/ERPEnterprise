using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Core.State;
using Erp.Setup.Sql;
using Erp.Setup.Tests.Checks;

namespace Erp.Setup.Tests.Sql;

/// <summary>Citirile C-10…C-20 (08, 5.4) pe SQL Server real, in container.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Criteriu", "08-IA-04")]
public sealed class SqlServerProbeIntegrationTests(SqlServerFixture sql)
{
    private const string CaseSensitive = RequirementsChecker.RequiredCollation;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<ISqlServerProbe> ConnectAsSa() => await sql.Connector().ConnectAsync(sql.Target("master"), Ct);

    private async Task<RequirementsReport> RunChecker(string database, string login = "sa", string? password = null, InstanceState? existing = null)
    {
        using var licenses = new TestLicenses();
        var license = CheckFixtures.WriteTemp(licenses.Create(TestLicenses.Payload()));
        try
        {
            string machine;
            await using (var probe = await ConnectAsSa())
            {
                machine = (await probe.GetServerInfoAsync(Ct)).MachineName;
            }

            var request = CheckFixtures.Request(license) with
            {
                Sql = new SqlRequest(sql.Server, database, "/var/opt/mssql/data", "/var/opt/mssql/data", AppIdentity.VirtualAccounts, null),
                Backup = CheckFixtures.Request(license).Backup with { Folder = "/var/opt/mssql/data/erp-backup-check" },
            };
            var target = CheckTarget.From(request, CheckMode.Install) with { Sql = sql.Target(database) };
            var checker = new RequirementsChecker(new FakeWindowsProbe { MachineName = machine }, sql.Connector(login, password), new FakeNetwork(),
                licenses.Reader, TimeProvider.System);
            return await checker.RunAsync(target, CheckFixtures.Manifest, new KitCheck(true, "kit"), existing, Ct);
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
        Assert.False(string.IsNullOrEmpty(info.MachineName));
        Assert.True((DateTime.UtcNow - info.UtcNow).Duration() < TimeSpan.FromMinutes(2));
        Assert.False(string.IsNullOrEmpty(info.DefaultDataPath));
        Assert.True(probe.TrustedServerCertificate);
    }

    [Fact]
    public async Task Failed_connection_is_reported_without_the_password()
    {
        const string password = "Gresit#Parola_123";

        var ex = await Assert.ThrowsAsync<SqlConnectFailedException>(() => sql.Connector("sa", password).ConnectAsync(sql.Target("master"), Ct));

        Assert.Contains("18456", ex.Message);
        Assert.DoesNotContain(password, ex.Message);
    }

    [Fact]
    public async Task Checker_against_a_real_server_reports_the_sql_checks()
    {
        var report = await RunChecker("ERP_IT_NEW");

        // Certificatul SQL din container e autosemnat: conexiunea e criptata, dar serverul nu e verificat (C-10, avertisment).
        Assert.Equal(CheckStatus.Warning, Result(report, "C-10").Status);
        Assert.True(report.TrustServerCertificate);
        Assert.Equal(CheckStatus.Warning, Result(report, "C-11").Status); // imaginea ruleaza Developer
        Assert.Equal(CheckStatus.Passed, Result(report, "C-12").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-14").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-15").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-16").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-18").Status);
        Assert.Equal(CheckStatus.Passed, Result(report, "C-20").Status);
        Assert.False(report.SqlIsRemote);
        Assert.Equal("/var/opt/mssql/data/erp-backup-check", report.BackupFolder);
    }

    [Fact]
    public async Task Login_without_server_rights_fails_C12_and_C16()
    {
        const string login = "erp_it_norights";
        const string password = "Fara#Drepturi_2026x";
        await sql.ExecuteAsync($"IF SUSER_ID(N'{login}') IS NULL CREATE LOGIN [{login}] WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;");

        var report = await RunChecker("ERP_IT_NEW", login, password);

        Assert.Equal(CheckStatus.Failed, Result(report, "C-12").Status);
        Assert.Equal(CheckStatus.Failed, Result(report, "C-16").Status);
        Assert.Equal(ExitCodes.RequirementsNotMet, report.ExitCode);
    }

    [Fact]
    public async Task Existing_database_with_data_is_refused_with_code_31()
    {
        const string database = "ERP_IT_DATA";
        await sql.ExecuteAsync($"IF DB_ID(N'{database}') IS NULL CREATE DATABASE {SqlText.QuoteName(database)} COLLATE {CaseSensitive};");
        await sql.ExecuteAsync("IF OBJECT_ID(N'dbo.Existing') IS NULL CREATE TABLE dbo.Existing (Id int NOT NULL);", database);

        var report = await RunChecker(database);

        Assert.Equal(CheckStatus.Failed, Result(report, "C-14").Status);
        Assert.Equal(ExitCodes.DatabaseExistsNotEmpty, report.ExitCode);
    }

    [Fact]
    public async Task Existing_database_with_another_collation_is_refused_with_code_32()
    {
        const string database = "ERP_IT_CI";
        await sql.ExecuteAsync($"IF DB_ID(N'{database}') IS NULL CREATE DATABASE {SqlText.QuoteName(database)} COLLATE SQL_Latin1_General_CP1_CI_AS;");

        var report = await RunChecker(database);

        Assert.Equal(CheckStatus.Failed, Result(report, "C-14").Status);
        Assert.Equal(ExitCodes.WrongDatabaseCollation, report.ExitCode);
    }

    [Fact]
    public async Task Existing_empty_case_sensitive_database_is_accepted()
    {
        const string database = "ERP_IT_EMPTY";
        await sql.ExecuteAsync($"IF DB_ID(N'{database}') IS NULL CREATE DATABASE {SqlText.QuoteName(database)} COLLATE {CaseSensitive};");

        var report = await RunChecker(database);

        Assert.Equal(CheckStatus.Passed, Result(report, "C-14").Status);
        Assert.Contains("goală", Result(report, "C-14").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Erp_database_of_the_same_installation_is_accepted_on_resume()
    {
        const string database = "ERP_IT_RESUME";
        var installId = Guid.NewGuid();
        await sql.ExecuteAsync($"IF DB_ID(N'{database}') IS NULL CREATE DATABASE {SqlText.QuoteName(database)} COLLATE {CaseSensitive};");
        await sql.ExecuteAsync($"""
            EXEC sys.sp_addextendedproperty @name = N'ErpProduct', @value = N'ERP';
            EXEC sys.sp_addextendedproperty @name = N'ErpInstance', @value = N'PROD';
            EXEC sys.sp_addextendedproperty @name = N'ErpInstallId', @value = N'{installId}';
            CREATE TABLE dbo.Partial (Id int NOT NULL);
            """, database);
        var existing = new InstanceState { Instance = "PROD", InstallId = installId, State = InstanceLifecycle.Installing, InstallRoot = "x" };

        Assert.Equal(CheckStatus.Passed, Result(await RunChecker(database, existing: existing), "C-14").Status);
        Assert.Equal(ExitCodes.DatabaseExistsNotEmpty, (await RunChecker(database)).ExitCode);
    }

    [Fact]
    public async Task Folders_are_created_by_the_SQL_Server_account_and_database_info_is_read()
    {
        await using var probe = await ConnectAsSa();
        var created = $"/var/opt/mssql/data/erp-it-{Guid.NewGuid():N}/FULL";

        await probe.CreateDirectoryAsync(created, Ct);
        await probe.CreateDirectoryAsync(created, Ct);

        var master = await probe.GetDatabaseAsync("master", Ct);
        Assert.True(master!.SizeBytes > 0);
        Assert.False(master.IsErp);
        Assert.Null(await probe.GetDatabaseAsync("ERP_IT_MISSING", Ct));
    }
}
