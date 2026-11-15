using Erp.Security.Passwords;
using Erp.Setup.Core;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Administrator;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Logins;
using Microsoft.Data.SqlClient;
using Serilog;

namespace Erp.Setup.Tests.Sql;

/// <summary>Pasul I-13 pe SQL Server real (8.8): IA-04, IA-05, IA-07 si fiecare THROW al procedurilor (TST-03).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class InitialAdminIntegrationTests(SqlServerFixture sql)
{
    private readonly CapturingSink logSink = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SqlOperator Operator() => new(sql.Target("master"), sql.SaPassword,
        new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(logSink).CreateLogger());

    private static InitialAdminRequest Request(string? password = null, string userName = "administrator") =>
        new(userName, "Administrator ERP", "it@grupulexemplu.ro", password, "2027.1.0", "SRV-ERP01");

    private async Task Deploy(string database, string instance, bool seed = true)
    {
        var op = Operator();
        await new DatabaseCreator(op).CreateAsync(new DatabaseCreateOptions(database, "IT", Guid.NewGuid(), null, null, 256, 256), Ct);
        await new BootstrapRunner(op).RunAsync(database, Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap"), Ct);
        using var kit = TempKitDatabase.FromRepository();
        var scripts = KitScripts.Load(kit.Root);
        var runner = new MigrationRunner(op.Log);
        await AsMigrator(database, instance, (builder, _) =>
        {
            runner.RunMigrations(builder.ConnectionString, scripts);
            runner.RunProgrammability(builder.ConnectionString, scripts);
            if (seed)
            {
                runner.RunSeed(builder.ConnectionString, scripts);
            }

            return Task.CompletedTask;
        });
    }

    private Task AsMigrator(string database, string instance, Func<SqlConnectionStringBuilder, CancellationToken, Task> work) =>
        new LoginManager(Operator(), SqlPrincipals.For(instance)).RunAsMigratorAsync(database, work, Ct);

    private async Task<InitialAdminResult> CreateAdmin(string database, string instance, InitialAdminRequest request)
    {
        InitialAdminResult? result = null;
        await AsMigrator(database, instance, async (builder, ct) =>
        {
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync(ct);
            result = await new InitialAdminCreator(Operator()).CreateAsync(connection, request, ct);
        });
        return result!;
    }

    private async Task<T?> Scalar<T>(string database, string query)
    {
        var builder = Erp.Setup.Sql.Probes.SqlServerConnector.BuildOperatorConnectionString(sql.Target(database), sql.SaPassword);
        builder.InitialCatalog = database;
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(Ct);
        return await SqlOperator.ScalarAsync<T>(connection, query, Ct);
    }

    private async Task<SqlException> ExecuteFails(string database, string statement)
    {
        return await Assert.ThrowsAsync<SqlException>(() => sql.ExecuteAsync(statement, database));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-04")]
    public async Task Exactly_one_active_administrator_with_admin_role_for_the_whole_installation()
    {
        await Deploy("ERP_IT_ADMIN", "ITADM");

        var result = await CreateAdmin("ERP_IT_ADMIN", "ITADM", Request());

        Assert.True(result.Created);
        Assert.True(result.PasswordGenerated);
        Assert.Equal(1, await Scalar<int>("ERP_IT_ADMIN", """
            SELECT COUNT(*) FROM core.UserRoleAssignment AS a
            JOIN core.[User] AS u ON u.Id = a.UserId JOIN core.Role AS r ON r.Id = a.RoleId
            WHERE r.Code = N'ADMIN' AND u.IsActive = 1 AND a.CompanyId IS NULL AND u.IsSystem = 1 AND u.IsEmployee = 0;
            """));
        Assert.Equal(1, await Scalar<int>("ERP_IT_ADMIN", """
            SELECT COUNT(*) FROM ids.UserAccount
            WHERE UserName = N'administrator' AND NormalizedUserName = N'ADMINISTRATOR'
              AND MustChangePassword = 1 AND TwoFactorRequired = 1 AND TwoFactorEnabled = 0 AND IsActive = 1;
            """));
        Assert.Equal(1, await Scalar<int>("ERP_IT_ADMIN", """
            SELECT COUNT(*) FROM core.[User] AS u JOIN ids.UserAccount AS a ON a.SubjectId = u.SubjectId WHERE u.UserName = N'administrator';
            """));
        Assert.Equal("""{"userName":"administrator","setupVersion":"2027.1.0","machine":"SRV-ERP01"}""",
            await Scalar<string>("ERP_IT_ADMIN", "SELECT Changes FROM core.AuditEntry WHERE Action = N'Core.User.CreateInitialAdmin';"));

        var hash = await Scalar<string>("ERP_IT_ADMIN", "SELECT PasswordHash FROM ids.UserAccount WHERE UserName = N'administrator';");
        Assert.True(IdentityPasswordHasher.Verify(hash!, result.TemporaryPassword!));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-05")]
    public async Task Password_is_not_in_logs_audit_or_logged_sql()
    {
        await Deploy("ERP_IT_ADMINLOG", "ITADML");
        const string password = "Wq7#Hn2p+Kd9!mXa4Rt_";

        var result = await CreateAdmin("ERP_IT_ADMINLOG", "ITADML", Request(password));

        Assert.False(result.PasswordGenerated);
        Assert.Equal(password, result.TemporaryPassword);
        Assert.DoesNotContain(password, result.ToString());
        Assert.DoesNotContain(password, logSink.Text);
        Assert.Contains("administratorul administrator a fost creat", logSink.Text);
        Assert.Equal(0, await Scalar<int>("ERP_IT_ADMINLOG", $"SELECT COUNT(*) FROM core.AuditEntry WHERE Changes LIKE N'%{password}%';"));
        Assert.Equal(0, await Scalar<int>("ERP_IT_ADMINLOG", $"SELECT COUNT(*) FROM ids.UserAccount WHERE PasswordHash LIKE N'%{password}%';"));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-07")]
    public async Task Running_again_creates_no_second_administrator_and_no_duplicate_seed()
    {
        await Deploy("ERP_IT_ADMIN2", "ITADM2");
        await CreateAdmin("ERP_IT_ADMIN2", "ITADM2", Request());

        await Deploy("ERP_IT_ADMIN2", "ITADM2");
        var second = await CreateAdmin("ERP_IT_ADMIN2", "ITADM2", Request(userName: "alt.admin"));

        Assert.False(second.Created);
        Assert.Null(second.TemporaryPassword);
        Assert.Contains(InitialAdminCreator.SkippedMessage, logSink.Text);
        Assert.Equal(1, await Scalar<int>("ERP_IT_ADMIN2", "SELECT COUNT(*) FROM ids.UserAccount;"));
        Assert.Equal(2, await Scalar<int>("ERP_IT_ADMIN2", "SELECT COUNT(*) FROM core.[User];"));
        Assert.Equal(4, await Scalar<int>("ERP_IT_ADMIN2", "SELECT COUNT(*) FROM core.Role WHERE IsSystem = 1;"));
        Assert.Equal(1, await Scalar<int>("ERP_IT_ADMIN2", "SELECT COUNT(*) FROM core.AuditEntry;"));
    }

    [Fact]
    public async Task System_user_and_roles_have_exact_codes()
    {
        await Deploy("ERP_IT_SYSTEM", "ITSYS");

        Assert.Equal("system", await Scalar<string>("ERP_IT_SYSTEM", "SELECT UserName FROM core.[User] WHERE Id = 1 AND IsSystem = 1 AND IsActive = 0;"));
        Assert.Equal(4, await Scalar<int>("ERP_IT_SYSTEM",
            "SELECT COUNT(*) FROM core.Role WHERE Code IN (N'ADMIN', N'NOMENCLATOARE', N'AUDITOR', N'STANDARD') AND IsSystem = 1;"));
        Assert.Equal(0, await Scalar<int>("ERP_IT_SYSTEM", "SELECT COUNT(*) FROM core.Role WHERE Code = N'admin';"));
    }

    [Fact]
    public async Task Missing_admin_role_fails_with_50016_and_rolls_back_the_account()
    {
        await Deploy("ERP_IT_NOROLE", "ITNOR", seed: false);

        var ex = await Assert.ThrowsAsync<SqlSetupException>(() => CreateAdmin("ERP_IT_NOROLE", "ITNOR", Request()));

        Assert.Equal(ExitCodes.SqlInstallError, ex.ExitCode);
        Assert.Contains("50016", ex.Message);
        Assert.Equal(0, await Scalar<int>("ERP_IT_NOROLE", "SELECT COUNT(*) FROM ids.UserAccount;"));
        Assert.Equal(1, await Scalar<int>("ERP_IT_NOROLE", "SELECT COUNT(*) FROM core.[User];"));
    }

    [Fact]
    public async Task Procedures_throw_their_documented_errors()
    {
        await Deploy("ERP_IT_THROW", "ITTHR");
        await CreateAdmin("ERP_IT_THROW", "ITTHR", Request());
        const string account = "EXEC ids.usp_UserAccount_CreateInitialAdmin @SubjectId = '{0}', @UserName = N'{1}', @Email = NULL, @PasswordHash = N'h', @SecurityStamp = N's', @ConcurrencyStamp = N'c';";

        Assert.Equal(64001, (await ExecuteFails("ERP_IT_THROW", string.Format(System.Globalization.CultureInfo.InvariantCulture, account, Guid.NewGuid(), "Admin2"))).Number);
        Assert.Equal(64002, (await ExecuteFails("ERP_IT_THROW", string.Format(System.Globalization.CultureInfo.InvariantCulture, account, Guid.NewGuid(), "administrator"))).Number);
        Assert.Equal(50014, (await ExecuteFails("ERP_IT_THROW",
            $"EXEC core.usp_User_CreateInitialAdmin @SubjectId = '{Guid.NewGuid()}', @UserName = N'admin2', @DisplayName = N'A', @Email = NULL, @SetupVersion = N'x', @MachineName = N'm';")).Number);
        Assert.Equal(50009, (await ExecuteFails("ERP_IT_THROW", "UPDATE core.AuditEntry SET IpAddress = '127.0.0.1';")).Number);
        Assert.Equal(50009, (await ExecuteFails("ERP_IT_THROW", "DELETE FROM core.AuditEntry;")).Number);
    }

    [Fact]
    public async Task Invalid_operator_password_or_user_name_is_refused_with_code_11()
    {
        await Deploy("ERP_IT_ADMINBAD", "ITADB");

        var weak = await Assert.ThrowsAsync<SqlSetupException>(() => CreateAdmin("ERP_IT_ADMINBAD", "ITADB", Request("short")));
        var upper = await Assert.ThrowsAsync<SqlSetupException>(() => CreateAdmin("ERP_IT_ADMINBAD", "ITADB", Request(userName: "Administrator")));

        Assert.Equal(ExitCodes.InvalidAnswersFile, weak.ExitCode);
        Assert.Equal(ExitCodes.InvalidAnswersFile, upper.ExitCode);
        Assert.DoesNotContain("short", weak.Message);
        Assert.Equal(0, await Scalar<int>("ERP_IT_ADMINBAD", "SELECT COUNT(*) FROM ids.UserAccount;"));
    }
}
