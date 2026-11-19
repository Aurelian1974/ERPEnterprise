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

    [Fact]
    public void Operator_connection_uses_windows_authentication_timeout_encryption_and_application_name()
    {
        var target = new Erp.Setup.Core.Checks.SqlTarget(@"SQL01\ERP", "ERP_PROD", "PROD");

        var builder = SqlServerConnector.BuildOperatorConnectionString(target);

        Assert.Equal("master", builder.InitialCatalog);
        Assert.Equal(15, builder.ConnectTimeout);
        Assert.Equal(Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Mandatory, builder.Encrypt);
        Assert.False(builder.TrustServerCertificate);
        Assert.Equal("ERP-PROD-Setup", builder.ApplicationName);
        Assert.True(builder.IntegratedSecurity);
        Assert.Equal(string.Empty, builder.UserID);
        Assert.True(SqlServerConnector.BuildOperatorConnectionString(target with { TrustServerCertificate = true }, "ERP_PROD").TrustServerCertificate);
    }
}
