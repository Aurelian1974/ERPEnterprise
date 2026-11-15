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
    public void Generated_password_has_32_characters_from_the_allowed_alphabet_with_two_of_each_category()
    {
        var passwords = Enumerable.Range(0, 200).Select(_ => SqlPasswordGenerator.Generate()).ToList();

        Assert.All(passwords, p =>
        {
            Assert.Equal(32, p.Length);
            Assert.All(p, c => Assert.Contains(c, SqlPasswordGenerator.Alphabet));
            Assert.True(p.Count(char.IsUpper) >= 2);
            Assert.True(p.Count(char.IsLower) >= 2);
            Assert.True(p.Count(char.IsDigit) >= 2);
            Assert.True(p.Count(c => "!#%*+-=?@_".Contains(c)) >= 2);
        });
        Assert.Equal(passwords.Count, passwords.Distinct().Count());
    }

    [Fact]
    public void Principals_are_lowercase_per_instance()
    {
        var principals = SqlPrincipals.For("PROD");

        Assert.Equal("erp_prod_app", principals.AppLogin);
        Assert.Equal("erp_prod_mig", principals.MigratorLogin);
        Assert.Equal(@"CONTOSO\svc-erp$", SqlPrincipals.For("PROD", Core.Answers.SqlAuthentication.Windows, @"CONTOSO\svc-erp$").AppLogin);
    }

    [Fact]
    public void Sql_login_script_creates_or_rotates_and_enables()
    {
        var script = LoginScripts.CreateOrAlterSqlLogin("erp_prod_app", "Abcdefgh12345678!#Abcdefgh123456", "ERP_PROD");

        Assert.Contains("IF SUSER_ID(N'erp_prod_app') IS NULL", script);
        Assert.Contains("CREATE LOGIN [erp_prod_app] WITH PASSWORD = N'Abcdefgh12345678!#Abcdefgh123456'", script);
        Assert.Contains("CHECK_POLICY = ON, CHECK_EXPIRATION = OFF", script);
        Assert.Contains("DEFAULT_DATABASE = [ERP_PROD], DEFAULT_LANGUAGE = [us_english]", script);
        Assert.Contains("ALTER LOGIN [erp_prod_app] WITH PASSWORD", script);
        Assert.Contains("ALTER LOGIN [erp_prod_app] ENABLE;", script);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("Abcdefgh12345678'; DROP LOGIN sa; --")]
    public void Unsafe_passwords_are_refused(string password)
    {
        Assert.Throws<SqlSetupException>(() => LoginScripts.CreateOrAlterSqlLogin("erp_prod_app", password, "ERP_PROD"));
    }

    [Fact]
    public void Windows_login_is_created_from_windows_and_validated()
    {
        Assert.Contains(@"CREATE LOGIN [CONTOSO\svc-erp$] FROM WINDOWS WITH DEFAULT_DATABASE = [ERP_PROD];",
            LoginScripts.CreateWindowsLogin(@"CONTOSO\svc-erp$", "ERP_PROD"));
        Assert.Throws<SqlSetupException>(() => LoginScripts.CreateWindowsLogin("svc-erp]; DROP LOGIN sa", "ERP_PROD"));
    }

    [Fact]
    public void Disable_script_is_idempotent()
    {
        Assert.Equal("IF SUSER_ID(N'erp_prod_mig') IS NOT NULL\n    ALTER LOGIN [erp_prod_mig] DISABLE;",
            LoginScripts.DisableLogin("erp_prod_mig").Replace("\r\n", "\n", StringComparison.Ordinal));
    }
}
