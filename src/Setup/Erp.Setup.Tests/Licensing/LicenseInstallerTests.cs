using Erp.Licensing;
using Erp.Setup.Core;
using Erp.Setup.Core.Answers;
using Erp.Setup.Core.Checks;
using Erp.Setup.Sql;
using Erp.Setup.Sql.Licensing;
using Erp.Setup.Tests.Checks;
using Serilog;

namespace Erp.Setup.Tests.Licensing;

public sealed class LicenseInstallerTests : IDisposable
{
    private readonly TestLicenses licenses = new();
    private readonly string dataRoot = Path.Combine(Path.GetTempPath(), "erp-licdata-" + Guid.NewGuid().ToString("N"));
    private readonly CapturingSink logSink = new();
    private readonly List<string> sources = [];

    public void Dispose()
    {
        licenses.Dispose();
        sources.ForEach(File.Delete);
        if (Directory.Exists(dataRoot))
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private LicenseInstaller Installer()
    {
        var target = new SqlTarget("SQL01", "ERP_PROD", SqlAuthentication.Windows, null, SqlEncrypt.Mandatory, false, "PROD");
        var log = new LoggerConfiguration().WriteTo.Sink(logSink).CreateLogger();
        return new LicenseInstaller(licenses.Reader, new SqlOperator(target, null, log));
    }

    private string Source(Erp.Licensing.License payload)
    {
        var path = CheckFixtures.WriteTemp(licenses.Create(payload));
        sources.Add(path);
        return path;
    }

    [Fact]
    public void Valid_license_is_copied_to_the_instance_license_folder()
    {
        var source = Source(TestLicenses.Payload());

        var result = Installer().VerifyAndCopy(source, dataRoot, "PROD");

        var target = LicenseInstaller.TargetPath(dataRoot, "PROD");
        Assert.True(result.IsValid);
        Assert.Equal(File.ReadAllText(source), File.ReadAllText(target));
        Assert.False(File.Exists(target + ".tmp"));
        Assert.EndsWith(Path.Combine("PROD", "license", "erp.lic"), target, StringComparison.Ordinal);
    }

    [Fact]
    public void Copying_the_same_license_again_leaves_the_file_unchanged()
    {
        var source = Source(TestLicenses.Payload());
        var installer = Installer();
        installer.VerifyAndCopy(source, dataRoot, "PROD");
        var target = LicenseInstaller.TargetPath(dataRoot, "PROD");
        var written = File.GetLastWriteTimeUtc(target);

        installer.VerifyAndCopy(source, dataRoot, "PROD");

        Assert.Equal(written, File.GetLastWriteTimeUtc(target));
    }

    [Fact]
    public void A_new_license_replaces_the_file()
    {
        var installer = Installer();
        installer.VerifyAndCopy(Source(TestLicenses.Payload()), dataRoot, "PROD");
        var replacement = Source(TestLicenses.Payload(modules: ["Core", "Finance", "Sales", "Hr"]));

        installer.VerifyAndCopy(replacement, dataRoot, "PROD");

        Assert.Equal(File.ReadAllText(replacement), File.ReadAllText(LicenseInstaller.TargetPath(dataRoot, "PROD")));
    }

    [Fact]
    public void Expired_license_is_refused_with_code_40_and_nothing_is_copied()
    {
        var ex = Assert.Throws<SqlSetupException>(() => Installer().VerifyAndCopy(Source(TestLicenses.Payload(validUntil: "2020-01-01")), dataRoot, "PROD"));

        Assert.Equal(ExitCodes.InvalidLicense, ex.ExitCode);
        Assert.False(File.Exists(LicenseInstaller.TargetPath(dataRoot, "PROD")));
    }

    [Fact]
    public void License_content_is_not_logged()
    {
        var source = Source(TestLicenses.Payload());

        Installer().VerifyAndCopy(source, dataRoot, "PROD");

        Assert.Contains("LIC-2026-000123", logSink.Text);
        Assert.DoesNotContain(File.ReadAllText(source)[..40], logSink.Text);
    }
}
