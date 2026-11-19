using Erp.Licensing;

namespace Erp.LicenseGen.Tests;

public sealed class FiscalCodeAndPolicyTests
{
    [Theory]
    [InlineData("10000008", "10000008")]
    [InlineData("RO10000008", "10000008")]
    [InlineData("ro 12345674", "12345674")]
    [InlineData(" RO 23456783 ", "23456783")]
    public void Fiscal_code_is_normalized_to_digits(string input, string expected) =>
        Assert.Equal(expected, FiscalCode.Normalize(input));

    [Theory]
    [InlineData("10000008", true)]
    [InlineData("12345674", true)]
    [InlineData("23456783", true)]
    [InlineData("34567892", true)]
    [InlineData("10000001", false)]
    [InlineData("12345678", false)]
    [InlineData("1", false)]
    [InlineData("12345678901", false)]
    [InlineData("RO-10000008", false)]
    public void Fiscal_code_check_digit_follows_the_anaf_key(string input, bool valid) =>
        Assert.Equal(valid, FiscalCode.IsValid(input));

    [Fact]
    public void License_contains_fiscal_code_with_or_without_ro()
    {
        var license = TestLicenses.Sample();

        Assert.True(license.ContainsFiscalCode("RO 12345674"));
        Assert.True(license.ContainsFiscalCode("12345674"));
        Assert.False(license.ContainsFiscalCode("45678906"));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-15")]
    public void Older_license_cannot_replace_the_active_one()
    {
        var active = TestLicenses.Sample(issuedAt: new DateOnly(2026, 11, 15));
        var older = TestLicenses.Sample(issuedAt: new DateOnly(2026, 10, 1), licenseId: "LIC-2026-000100");
        var newer = TestLicenses.Sample(issuedAt: new DateOnly(2026, 12, 1), licenseId: "LIC-2026-000140");

        Assert.Equal("Licența încărcată este mai veche decât cea activă (emisă la 15.11.2026).", LicensePolicy.CheckReplacement(active, older));
        Assert.Null(LicensePolicy.CheckReplacement(active, newer));
        Assert.Null(LicensePolicy.CheckReplacement(active, active));
        Assert.Null(LicensePolicy.CheckReplacement(null, older));
    }

    [Fact]
    [Trait("Criteriu", "08-IA-15")]
    public void Updates_until_refuses_kits_released_later()
    {
        var limited = TestLicenses.Sample(updatesUntil: new DateOnly(2026, 6, 30));

        Assert.Equal(
            "Licența include actualizări lansate până la 30.06.2026; kitul 2027.1.0 a fost lansat la 15.01.2027. Folosiți un kit lansat până la acea dată sau cereți furnizorului prelungirea.",
            LicensePolicy.CheckKitRelease(limited, new DateOnly(2027, 1, 15), "2027.1.0"));
        Assert.Null(LicensePolicy.CheckKitRelease(limited, new DateOnly(2026, 6, 30), "2026.2.0"));
        Assert.Null(LicensePolicy.CheckKitRelease(TestLicenses.Sample(), new DateOnly(2030, 1, 1), "2030.1.0"));
    }
}
