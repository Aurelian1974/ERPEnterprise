using System.Security.Cryptography;
using Erp.Licensing;
using Erp.Setup.Tests.Checks;

namespace Erp.Setup.Tests.Licensing;

public sealed class LicenseVerifierTests : IDisposable
{
    private readonly TestLicenses licenses = new();

    public void Dispose() => licenses.Dispose();

    private LicenseVerificationResult Verify(string content) =>
        new LicenseVerifier(licenses.PublicKeys, TimeProvider.System).Verify(content);

    [Fact]
    public void Valid_license_is_accepted_with_its_payload()
    {
        var result = Verify(licenses.Create(TestLicenses.Payload()));

        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Equal("LIC-2026-000123", result.Payload!.LicenseId);
        Assert.Equal(new DateOnly(2099, 12, 31), result.Payload.ValidUntil);
        Assert.Equal(5, result.Payload.Limits.Companies);
        Assert.Empty(result.UnknownModules);
    }

    [Fact]
    public void Expired_license_is_refused()
    {
        var result = Verify(licenses.Create(TestLicenses.Payload(validUntil: "2020-01-01")));

        Assert.Equal(LicenseStatus.Expired, result.Status);
        Assert.Contains("2020-01-01", result.Message);
    }

    [Fact]
    public void License_signed_with_another_key_is_refused()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal(LicenseStatus.InvalidSignature, Verify(licenses.Create(TestLicenses.Payload(), signWith: other)).Status);
    }

    [Fact]
    public void Unknown_key_id_is_refused()
    {
        var result = Verify(licenses.Create(TestLicenses.Payload(), keyId: "lic-1999-01"));

        Assert.Equal(LicenseStatus.UnknownKey, result.Status);
        Assert.Contains("lic-1999-01", result.Message);
    }

    [Theory]
    [InlineData("erp")]
    [InlineData("OTHER")]
    public void Other_product_is_refused_with_exact_comparison(string product)
    {
        Assert.Equal(LicenseStatus.WrongProduct, Verify(licenses.Create(TestLicenses.Payload(product: product))).Status);
    }

    [Fact]
    public void Modified_payload_is_refused()
    {
        var content = licenses.Create(TestLicenses.Payload());
        var tampered = content.Replace("\"payload\":\"e", "\"payload\":\"f", StringComparison.Ordinal);

        Assert.NotEqual(content, tampered);
        Assert.NotEqual(LicenseStatus.Valid, Verify(tampered).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"payload\":\"!!\",\"signature\":\"x\",\"keyId\":\"lic-test-01\"}")]
    public void Malformed_file_is_refused(string content)
    {
        Assert.Equal(LicenseStatus.InvalidFormat, Verify(content).Status);
    }

    [Fact]
    public void Unknown_modules_are_reported()
    {
        var result = Verify(licenses.Create(TestLicenses.Payload(modules: ["Core", "Robotics", "sales"])));

        Assert.True(result.IsValid);
        Assert.Equal(["Robotics", "sales"], result.UnknownModules);
    }
}
