using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Licensing;
using Erp.Setup.Tests.Checks;
using Serilog;

namespace Erp.Setup.Tests.Licensing;

/// <summary>Verificarea licentei la instalare (08, 4.2, 4.5, C-17); licenta nu se mai copiaza pe disc (D18).</summary>
public sealed class LicenseInstallerTests : IDisposable
{
    private readonly TestLicenses licenses = new();
    private readonly List<string> files = [];

    public void Dispose()
    {
        licenses.Dispose();
        foreach (var file in files.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    private LicenseInstaller Installer()
    {
        var target = new SqlTarget("localhost", "ERP_PROD", "PROD");
        return new LicenseInstaller(licenses.Reader, new SqlOperator(target, new LoggerConfiguration().CreateLogger()));
    }

    private string Source(License payload)
    {
        var path = CheckFixtures.WriteTemp(licenses.Create(payload));
        files.Add(path);
        return path;
    }

    [Fact]
    public void Valid_license_is_verified_and_its_exact_bytes_are_kept_for_the_database()
    {
        var source = Source(TestLicenses.Payload());

        var verified = Installer().Verify(source);

        Assert.True(verified.Result.IsValid);
        Assert.Equal(File.ReadAllBytes(source), verified.File);
        Assert.Equal("LIC-2026-000123", verified.License.LicenseId);
    }

    [Fact]
    public void Expired_license_is_refused_with_code_40()
    {
        var ex = Assert.Throws<SqlSetupException>(() => Installer().Verify(Source(TestLicenses.Payload(validUntil: "2020-01-01"))));

        Assert.Equal(ExitCodes.InvalidLicense, ex.ExitCode);
        Assert.Contains("a expirat", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_file_is_refused_with_code_40() =>
        Assert.Equal(ExitCodes.InvalidLicense, Assert.Throws<SqlSetupException>(() => Installer().Verify("/nu/exista.lic")).ExitCode);

    [Fact]
    [Trait("Criteriu", "08-IA-15")]
    public void Kit_released_after_updates_until_is_refused_with_code_41()
    {
        var limited = TestLicenses.Payload() with { UpdatesUntil = new DateOnly(2026, 6, 30) };

        var ex = Assert.Throws<SqlSetupException>(() => Installer().Verify(Source(limited), new DateOnly(2027, 1, 15), "2027.1.0"));

        Assert.Equal(ExitCodes.LicenseExcludesKit, ex.ExitCode);
        Assert.Contains("kitul 2027.1.0 a fost lansat la 15.01.2027", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Kit_released_before_updates_until_is_accepted() =>
        Assert.True(Installer().Verify(Source(TestLicenses.Payload() with { UpdatesUntil = new DateOnly(2027, 6, 30) }),
            new DateOnly(2027, 1, 15), "2027.1.0").Result.IsValid);
}
