using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Erp.LicenseGen.Core;
using Erp.Licensing;

namespace Erp.LicenseGen.Tests;

[Trait("Criteriu", "08-IA-02")]
public sealed class LicenseReaderTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 12, 1);
    private readonly TestLicenses keys = new();

    public void Dispose() => keys.Dispose();

    [Fact]
    public void License_written_with_the_active_key_is_read_back_unchanged()
    {
        var original = TestLicenses.Sample(validUntil: new DateOnly(2027, 12, 31));

        var result = keys.Reader.Read(LicenseWriter.Write(original, keys.Active), Today);

        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Equal(1, result.KeyId);
        Assert.Equal(original.FiscalCodes, result.License!.FiscalCodes);
        Assert.Equal(original.Modules, result.License.Modules);
        Assert.Equal(original.Limits, result.License.Limits);
        Assert.Equal(4, result.License.Companies);
    }

    [Fact]
    public void License_written_with_the_reserve_key_is_accepted()
    {
        var result = keys.Reader.Read(LicenseWriter.Write(TestLicenses.Sample(), keys.Reserve), Today);

        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Equal(2, result.KeyId);
    }

    [Fact]
    public void Any_single_modified_byte_is_refused()
    {
        var file = LicenseWriter.Write(TestLicenses.Sample(), keys.Active);

        for (var i = 0; i < file.Length; i++)
        {
            var tampered = (byte[])file.Clone();
            tampered[i] ^= 0x01;
            Assert.False(keys.Reader.Read(tampered, Today).IsAuthentic, $"Octetul {i} modificat a fost acceptat.");
        }
    }

    [Fact]
    public void License_signed_by_another_key_with_the_same_key_id_is_refused()
    {
        using var forger = LicenseSigningKey.Generate(1);

        var result = keys.Reader.Read(LicenseWriter.Write(TestLicenses.Sample(), forger), Today);

        Assert.Equal(LicenseStatus.NotIssuedByVendor, result.Status);
        Assert.Equal("Licența nu este emisă de producător sau a fost modificată.", result.Message);
    }

    [Fact]
    public void Unknown_key_id_is_refused_as_not_issued_by_the_vendor()
    {
        using var unknown = LicenseSigningKey.Generate(9);

        Assert.Equal(LicenseStatus.NotIssuedByVendor, keys.Reader.Read(LicenseWriter.Write(TestLicenses.Sample(), unknown), Today).Status);
    }

    [Fact]
    public void Fiscal_codes_and_customer_name_do_not_appear_in_clear_in_the_file()
    {
        var file = LicenseWriter.Write(TestLicenses.Sample(), keys.Active);
        var asLatin1 = Encoding.Latin1.GetString(file);

        foreach (var code in TestLicenses.FiscalCodes.Split(':'))
        {
            Assert.DoesNotContain(code, asLatin1, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Grupul", asLatin1, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(105)]
    public void Truncated_or_foreign_files_are_not_licenses(int length)
    {
        var result = keys.Reader.Read(new byte[length], Today);

        Assert.Equal(LicenseStatus.NotALicense, result.Status);
        Assert.Equal("Fișierul nu este o licență ERP.", result.Message);
    }

    [Fact]
    public void Wrong_format_version_is_not_a_license()
    {
        var file = LicenseWriter.Write(TestLicenses.Sample(), keys.Active);
        file[8] = 2;

        Assert.Equal(LicenseStatus.NotALicense, keys.Reader.Read(file, Today).Status);
    }

    [Fact]
    public void Authentic_file_with_invalid_content_is_damaged()
    {
        // Semnat corect, dar produsul e altul: verificarea 4 (08, 4.2).
        var json = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(LicenseFormat.Serialize(TestLicenses.Sample()))
            .Replace("\"product\":\"ERP\"", "\"product\":\"ALT\"", StringComparison.Ordinal));

        var result = keys.Reader.Read(Compose(json, keys.Active), Today);

        Assert.Equal(LicenseStatus.Damaged, result.Status);
        Assert.Equal("Licența este deteriorată.", result.Message);
    }

    [Fact]
    public void Authentic_file_with_a_bad_check_digit_is_damaged()
    {
        var json = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(LicenseFormat.Serialize(TestLicenses.Sample()))
            .Replace("10000008", "10000001", StringComparison.Ordinal));

        Assert.Equal(LicenseStatus.Damaged, keys.Reader.Read(Compose(json, keys.Active), Today).Status);
    }

    [Fact]
    public void Expired_license_is_reported_with_its_content_for_the_grace_period()
    {
        var result = keys.Reader.Read(LicenseWriter.Write(TestLicenses.Sample(validUntil: new DateOnly(2026, 11, 30)), keys.Active), Today);

        Assert.Equal(LicenseStatus.Expired, result.Status);
        Assert.True(result.IsAuthentic);
        Assert.NotNull(result.License);
        Assert.Equal("Licența a expirat la 30.11.2026. Cereți furnizorului o licență nouă.", result.Message);
    }

    [Fact]
    public void Unlimited_license_never_expires()
    {
        var result = keys.Reader.Read(LicenseWriter.Write(TestLicenses.Sample(validUntil: null), keys.Active), new DateOnly(2099, 1, 1));

        Assert.Equal(LicenseStatus.Valid, result.Status);
        Assert.Null(result.License!.ValidUntil);
    }

    [Fact]
    public void Writer_refuses_a_license_that_breaks_the_content_rules()
    {
        Assert.Throws<ArgumentException>(() => LicenseWriter.Write(TestLicenses.Sample(fiscalCodes: "10000008:10000008"), keys.Active));
        Assert.Throws<ArgumentException>(() => LicenseWriter.Write(TestLicenses.Sample(fiscalCodes: "RO10000008"), keys.Active));
    }

    [Fact]
    public void Embedded_source_line_restores_the_same_keys()
    {
        var line = keys.Active.ToSourceLine();
        var parts = Regex.Matches(line, "\"([^\"]+)\"").Select(m => Convert.FromBase64String(m.Groups[1].Value)).ToArray();

        Assert.StartsWith("new(1, ", line, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(keys.Active.AesKey), line, StringComparison.Ordinal);
        Assert.Equal(keys.Active.AesKey, LicenseKeys.Xor(parts[1], parts[2]));
        Assert.Equal(keys.Active.PublicKey, parts[0]);
    }

    /// <summary>Un fisier semnat corect, cu un continut ales de test (ocoleste regulile writer-ului).</summary>
    private static byte[] Compose(byte[] json, LicenseSigningKey key)
    {
        var file = new byte[LicenseFormat.TotalLength(json.Length)];
        LicenseFormat.Marker.CopyTo(file);
        file[8] = LicenseFormat.Version;
        file[9] = key.KeyId;
        RandomNumberGenerator.Fill(file.AsSpan(10, 12));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(22, 4), (uint)json.Length);
        using (var aes = new AesGcm(key.AesKey, 16))
        {
            aes.Encrypt(file.AsSpan(10, 12), json, file.AsSpan(26, json.Length), file.AsSpan(26 + json.Length, 16), file.AsSpan(0, 26));
        }

        var signed = 26 + json.Length + 16;
        key.Sign(file.AsSpan(0, signed)).CopyTo(file.AsSpan(signed));
        return file;
    }
}
