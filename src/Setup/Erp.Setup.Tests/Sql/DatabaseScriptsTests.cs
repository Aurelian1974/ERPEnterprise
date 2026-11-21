using Erp.Setup.Core.Answers;
using Erp.Setup.Core;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Database;
using Erp.Setup.Sql.Logins;

namespace Erp.Setup.Tests.Sql;

public sealed class DatabaseScriptsTests
{
    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Create_database_script_is_case_sensitive_with_quoted_name_and_invariant_sizes()
    {
        var script = DatabaseScripts.CreateDatabase("ERP_PROD", @"D:\SQLData\ERP_PROD.mdf", @"L:\SQLLog\ERP_PROD_log.ldf", 1024, 512);

        Assert.StartsWith("CREATE DATABASE [ERP_PROD]", script, StringComparison.Ordinal);
        Assert.Contains(@"FILENAME = N'D:\SQLData\ERP_PROD.mdf'", script);
        Assert.Contains("NAME = N'ERP_PROD_log'", script);
        Assert.Contains("SIZE = 1024MB", script);
        Assert.Contains("SIZE = 512MB", script);
        Assert.EndsWith("COLLATE Latin1_General_100_CS_AS_SC_UTF8;", script.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-02")]
    public void Settings_include_rcsi_full_recovery_and_compatibility_160()
    {
        var statements = DatabaseScripts.Settings("ERP_PROD").Select(s => s.Sql).ToList();

        Assert.Contains("ALTER DATABASE [ERP_PROD] SET RECOVERY FULL;", statements);
        Assert.Contains("ALTER DATABASE [ERP_PROD] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;", statements);
        Assert.Contains("ALTER DATABASE [ERP_PROD] SET COMPATIBILITY_LEVEL = 160;", statements);
        Assert.Contains("ALTER DATABASE [ERP_PROD] SET PAGE_VERIFY CHECKSUM;", statements);
        Assert.Equal(12, statements.Count);
    }

    [Theory]
    [InlineData("1ERP")]
    [InlineData("ERP]; DROP DATABASE x; --")]
    [InlineData("ER")]
    public void Invalid_database_names_are_refused(string name)
    {
        Assert.Throws<SqlSetupException>(() => DatabaseScripts.ValidateDatabaseName(name));
    }

    [Theory]
    [InlineData(@"D:\SQL'Data")]
    [InlineData(@"D:\SQL;Data")]
    [InlineData(@"D:\SQL--Data")]
    [InlineData("SQLData")]
    [InlineData("D:\\SQL\u0001Data")]
    [InlineData(@"D:\SQL|Data")]
    public void Invalid_sql_paths_are_refused(string path)
    {
        var ex = Assert.Throws<SqlSetupException>(() => DatabaseScripts.ValidatePath(path));

        Assert.Equal(ExitCodes.SqlInstallError, ex.ExitCode);
    }

    [Theory]
    [InlineData(@"D:\SQLData", @"D:\SQLData\ERP.mdf")]
    [InlineData(@"D:\SQLData\", @"D:\SQLData\ERP.mdf")]
    [InlineData(@"\\nas\sql", @"\\nas\sql\ERP.mdf")]
    [InlineData("/var/opt/mssql/data/", "/var/opt/mssql/data/ERP.mdf")]
    public void File_path_uses_the_folder_separator(string folder, string expected)
    {
        Assert.Equal(expected, DatabaseScripts.CombinePath(folder, "ERP.mdf"));
    }

    [Fact]
    public void Owner_is_quoted()
    {
        Assert.Equal("ALTER AUTHORIZATION ON DATABASE::[ERP_PROD] TO [sa]];x];", DatabaseScripts.AlterAuthorization("ERP_PROD", "sa];x"));
    }

    [Fact]
    public void Batches_are_split_on_go_lines_only()
    {
        var batches = BootstrapRunner.SplitBatches("SELECT 1;\r\nGO\r\nSELECT N'GO';\n  go  \nSELECT 2; -- GOOD\nGO\n");

        Assert.Equal(["SELECT 1;", "SELECT N'GO';", "SELECT 2; -- GOOD"], batches);
    }

    [Fact]
    public void Bootstrap_scripts_create_every_schema_and_role_of_the_module_order()
    {
        var folder = Path.Combine(TestPaths.RepositoryRoot(), "src", "Setup", "Erp.Setup.Sql", "Database", "Bootstrap");
        var schemas = File.ReadAllText(Path.Combine(folder, "001_Schemas.sql"));
        var roles = File.ReadAllText(Path.Combine(folder, "002_Roles.sql"));

        foreach (var (schema, index) in ModuleOrder.Schemas.Select((s, i) => (s, i + 1)))
        {
            Assert.Contains($"({index}, N'{schema}')", schemas);
            Assert.Contains($"({index}, N'{schema}')", roles);
        }
    }
}

public sealed class LoginScriptsTests
{
    [Fact]
    public void Same_machine_uses_the_virtual_accounts_of_the_pool_and_the_service()
    {
        var principals = AppPrincipals.For("PROD", AppIdentity.VirtualAccounts, null);

        Assert.Equal(
            [new AppPrincipal(@"IIS APPPOOL\ERP-PROD", "erp_web"), new AppPrincipal(@"NT SERVICE\ErpWorker-PROD", "erp_worker")],
            principals);
    }

    [Theory]
    [InlineData(AppIdentity.Gmsa, @"GRUPULEXEMPLU\erp-prod$")]
    [InlineData(AppIdentity.ComputerAccount, @"GRUPULEXEMPLU\SRV-ERP$")]
    public void Remote_SQL_uses_one_login_for_the_account(AppIdentity identity, string account)
    {
        Assert.Equal([new AppPrincipal(account, "erp_app")], AppPrincipals.For("PROD", identity, account));
    }

    [Fact]
    public void Windows_login_is_created_from_windows_only_when_missing_and_enabled()
    {
        var script = LoginScripts.CreateWindowsLogin(@"GRUPULEXEMPLU\erp-prod$", "ERP_PROD");

        Assert.Contains(@"IF SUSER_ID(N'GRUPULEXEMPLU\erp-prod$') IS NULL", script);
        Assert.Contains(@"CREATE LOGIN [GRUPULEXEMPLU\erp-prod$] FROM WINDOWS WITH DEFAULT_DATABASE = [ERP_PROD], DEFAULT_LANGUAGE = [us_english];", script);
        Assert.Contains(@"ALTER LOGIN [GRUPULEXEMPLU\erp-prod$] ENABLE;", script);
        Assert.DoesNotContain("PASSWORD", script);
    }

    [Theory]
    [InlineData("erp_prod_app")]
    [InlineData("svc-erp]; DROP LOGIN sa")]
    [InlineData(@"DOMENIU\x'; --")]
    public void Unsafe_or_non_windows_logins_are_refused(string account)
    {
        Assert.Throws<SqlSetupException>(() => LoginScripts.CreateWindowsLogin(account, "ERP_PROD"));
    }

    [Fact]
    public void User_is_created_or_remapped_to_the_login_after_a_restore()
    {
        var script = LoginScripts.CreateOrRemapUser("erp_app", @"GRUPULEXEMPLU\erp-prod$", "core").Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains(@"CREATE USER [erp_app] FOR LOGIN [GRUPULEXEMPLU\erp-prod$] WITH DEFAULT_SCHEMA = [core];", script);
        Assert.Contains(@"ALTER USER [erp_app] WITH LOGIN = [GRUPULEXEMPLU\erp-prod$], NAME = [erp_app];", script);
        Assert.Contains("N'[erp_app]'", script);
    }

    [Fact]
    public void Disable_script_is_idempotent()
    {
        Assert.Equal("IF SUSER_ID(N'erp_prod_mig') IS NOT NULL\n    ALTER LOGIN [erp_prod_mig] DISABLE;",
            LoginScripts.DisableLogin("erp_prod_mig").Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
