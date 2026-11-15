using Erp.Setup.Sql;
using Erp.Setup.Sql.Probes;

namespace Erp.Setup.Tests.Sql;

public sealed class SqlTextTests
{
    [Theory]
    [InlineData("ERP_PROD", "[ERP_PROD]")]
    [InlineData("a]b", "[a]]b]")]
    public void QuoteName_brackets_and_doubles_closing_bracket(string name, string expected)
    {
        Assert.Equal(expected, SqlText.QuoteName(name));
    }

    [Fact]
    public void QuoteName_refuses_empty_or_too_long_names()
    {
        Assert.ThrowsAny<ArgumentException>(() => SqlText.QuoteName(""));
        Assert.Throws<ArgumentException>(() => SqlText.QuoteName(new string('a', 129)));
    }

    [Theory]
    [InlineData(@"D:\SQLData", @"N'D:\SQLData'")]
    [InlineData("O'Brien", "N'O''Brien'")]
    [InlineData("Șantier", "N'Șantier'")]
    public void SqlLiteral_is_unicode_with_doubled_apostrophe(string value, string expected)
    {
        Assert.Equal(expected, SqlText.SqlLiteral(value));
    }

    [Theory]
    [InlineData(@"D:\SQLData\ERP", @"D:\SQLData")]
    [InlineData(@"D:\SQLData\", @"D:\")]
    [InlineData(@"D:\SQLData", @"D:\")]
    [InlineData(@"D:\", null)]
    [InlineData(@"\\nas\backup\sql", @"\\nas\backup")]
    [InlineData(@"\\nas\backup", null)]
    [InlineData("/var/opt/mssql", "/var/opt")]
    [InlineData("/var", "/")]
    [InlineData("/", null)]
    public void Parent_folder_walks_up_to_the_root(string path, string? expected)
    {
        Assert.Equal(expected, SqlServerProbe.ParentOf(path));
    }

    [Fact]
    public void Operator_connection_string_uses_master_timeout_encryption_and_application_name()
    {
        var target = new Erp.Setup.Core.Checks.SqlTarget(@"SQL01\ERP", "ERP_PROD", Erp.Setup.Core.Answers.SqlAuthentication.Sql, "op",
            Erp.Setup.Core.Answers.SqlEncrypt.Strict, false, "PROD");

        var builder = SqlServerConnector.BuildOperatorConnectionString(target, "p@ss");

        Assert.Equal("master", builder.InitialCatalog);
        Assert.Equal(15, builder.ConnectTimeout);
        Assert.Equal(Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Strict, builder.Encrypt);
        Assert.Equal("ERP-PROD-Setup", builder.ApplicationName);
        Assert.Equal("op", builder.UserID);
        Assert.False(builder.IntegratedSecurity);
    }
}
