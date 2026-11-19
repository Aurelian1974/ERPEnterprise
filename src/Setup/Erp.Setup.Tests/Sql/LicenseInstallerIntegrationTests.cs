using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Licensing;
using Erp.Setup.Sql.Logins;
using Erp.Setup.Tests.Checks;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>Pasul I-12 pe SQL Server real: core.usp_InstallationLicense_Save cu loginul de migrare.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LicenseInstallerIntegrationTests(SqlServerFixture sql) : IDisposable
{
    private readonly TestLicenses licenses = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => licenses.Dispose();

    private SqlOperator Operator() => sql.Operator();

    private VerifiedLicense Verified(License payload)
    {
        var file = licenses.Create(payload);
        return new VerifiedLicense(file, licenses.Reader.Read(file));
    }

    private async Task<string> DeployedDatabase(string name, string instance)
    {
        var op = Operator();
        await new DatabaseCreator(op).CreateAsync(new DatabaseCreateOptions(name, "IT", Guid.NewGuid(), null, null, 256, 256), Ct);
        await new BootstrapRunner(op).RunAsync(name, Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap"), Ct);
        using var kit = TempKitDatabase.FromRepository();
        var scripts = KitScripts.Load(kit.Root);
        var runner = new MigrationRunner(new LoggerConfiguration().CreateLogger());
        runner.RunMigrations(op.ConnectionString(name), scripts);
        runner.RunProgrammability(op.ConnectionString(name), scripts);
        return name;
    }

    private async Task<int> SaveAsMigrator(string database, string instance, VerifiedLicense license)
    {
        var op = Operator();
        await using var connection = await op.OpenAsync(database, Ct);
        return await new LicenseInstaller(licenses.Reader, op).SaveAsync(connection, license, Ct);
    }

    private async Task<T?> Scalar<T>(string database, string query)
    {
        await using var connection = new SqlConnection(sql.ConnectionString(database));
        await connection.OpenAsync(Ct);
        return await SqlOperator.ScalarAsync<T>(connection, query, Ct);
    }

    [Fact]
    public async Task License_is_saved_once_and_a_new_license_becomes_current()
    {
        var database = await DeployedDatabase("ERP_IT_LIC", "ITLIC");
        var license = Verified(TestLicenses.Payload());

        var first = await SaveAsMigrator(database, "ITLIC", license);
        var again = await SaveAsMigrator(database, "ITLIC", license);

        Assert.Equal(first, again);
        Assert.Equal(1, await Scalar<int>(database, "SELECT COUNT(*) FROM core.InstallationLicense;"));
        Assert.Equal("Grupul Exemplu", await Scalar<string>(database, "SELECT CustomerName FROM core.InstallationLicense WHERE IsCurrent = 1;"));
        Assert.Equal("[\"Core\",\"Finance\",\"Sales\"]", await Scalar<string>(database, "SELECT Modules FROM core.InstallationLicense WHERE IsCurrent = 1;"));
        Assert.Equal(300, await Scalar<int>(database, "SELECT LimitPayrollEmployees FROM core.InstallationLicense WHERE IsCurrent = 1;"));
        Assert.Equal(TestLicenses.KeyId, await Scalar<string>(database, "SELECT KeyId FROM core.InstallationLicense WHERE IsCurrent = 1;"));
        Assert.Equal(1, await Scalar<int>(database, "SELECT CreatedBy FROM core.InstallationLicense WHERE IsCurrent = 1;"));

        var replacement = await SaveAsMigrator(database, "ITLIC", Verified(TestLicenses.Payload(modules: ["Core", "Hr"])));

        Assert.NotEqual(first, replacement);
        Assert.Equal(2, await Scalar<int>(database, "SELECT COUNT(*) FROM core.InstallationLicense;"));
        Assert.Equal(replacement, await Scalar<int>(database, "SELECT Id FROM core.InstallationLicense WHERE IsCurrent = 1;"));
        Assert.NotNull(await Scalar<DateTime?>(database, $"SELECT ModifiedAt FROM core.InstallationLicense WHERE Id = {first};"));
    }

    [Fact]
    public async Task License_with_validity_before_issue_date_is_refused_with_50017_and_code_40()
    {
        var database = await DeployedDatabase("ERP_IT_LICDATES", "ITLICD");
        var license = Verified(TestLicenses.Payload(validUntil: "2099-01-01"));
        var badDates = license with { Result = license.Result with { License = license.License with { IssuedAt = new DateOnly(2099, 6, 1) } } };

        var ex = await Assert.ThrowsAsync<SqlSetupException>(() => SaveAsMigrator(database, "ITLICD", badDates));

        Assert.Equal(ExitCodes.InvalidLicense, ex.ExitCode);
        Assert.Contains("50017", ex.Message);
        Assert.Equal(0, await Scalar<int>(database, "SELECT COUNT(*) FROM core.InstallationLicense;"));
    }

    [Fact]
    public async Task App_login_can_read_the_current_license()
    {
        var database = await DeployedDatabase("ERP_IT_LICAPP", "ITLICA");
        await SaveAsMigrator(database, "ITLICA", Verified(TestLicenses.Payload()));
        await using var app = new SqlConnection(await sql.CreateAppLoginAsync(database, "erp_it_licapp", Ct));
        await app.OpenAsync(Ct);

        Assert.Equal("LIC-2026-000123", await SqlOperator.ScalarAsync<string>(app, "SELECT LicenseId FROM core.InstallationLicense WHERE IsCurrent = 1;", Ct));
    }
}
