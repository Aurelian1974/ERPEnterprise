using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Erp.Setup.Core;
using Erp.Setup.Core.Kit;

namespace Erp.Setup.Tests.Kit;

public sealed class KitVerifierTests : IDisposable
{
    private readonly string kitRoot = Path.Combine(Path.GetTempPath(), "erp-kit-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public KitVerifierTests()
    {
        Directory.CreateDirectory(kitRoot);
        AddFile("app/host/Erp.Host.dll", "host");
        AddFile("database/migrations/core/2026.11.001_Create_Company.sql", "CREATE TABLE core.Company (Id int);");
        AddFile("templates/answers.example.json", "{}");
        AddFile("ErpSetup.exe", "setup");
        WriteSignedManifest();
    }

    public void Dispose()
    {
        signingKey.Dispose();
        Directory.Delete(kitRoot, recursive: true);
    }

    private KitVerificationResult Verify(string? publicKeyPem = null) =>
        new KitVerifier(publicKeyPem ?? signingKey.ExportSubjectPublicKeyInfoPem()).Verify(kitRoot);

    [Fact]
    [Trait("Criteriu", "08-IA-13")]
    public void Intact_kit_is_accepted()
    {
        var result = Verify();

        Assert.True(result.Succeeded, string.Join("; ", result.Details));
        Assert.Equal("2027.1.0", result.Manifest!.Version);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-13")]
    public void Kit_file_with_one_modified_byte_is_rejected_with_code_21()
    {
        FlipFirstByte("app/host/Erp.Host.dll");

        var result = Verify();

        Assert.Equal(ExitCodes.KitFilesMissingOrModified, result.ExitCode);
        Assert.Equal(KitVerifier.FilesFailureMessage, result.Message);
        Assert.Contains("modificat: app/host/Erp.Host.dll", result.Details);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-13")]
    public void Manifest_with_one_modified_byte_is_rejected_with_code_20()
    {
        var path = Path.Combine(kitRoot, KitVerifier.ManifestFileName);
        var bytes = File.ReadAllBytes(path);
        var index = Array.IndexOf(bytes, (byte)'7');
        bytes[index] = (byte)'8';
        File.WriteAllBytes(path, bytes);

        var result = Verify();

        Assert.Equal(ExitCodes.InvalidKitSignature, result.ExitCode);
        Assert.Equal(KitVerifier.SignatureFailureMessage, result.Message);
    }

    [Fact]
    public void Kit_signed_with_another_key_is_rejected_with_code_20()
    {
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.Equal(ExitCodes.InvalidKitSignature, Verify(otherKey.ExportSubjectPublicKeyInfoPem()).ExitCode);
    }

    [Fact]
    public void Public_key_on_another_curve_is_rejected_with_code_20()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        Assert.Equal(ExitCodes.InvalidKitSignature, Verify(p384.ExportSubjectPublicKeyInfoPem()).ExitCode);
    }

    [Theory]
    [InlineData(KitVerifier.ManifestFileName)]
    [InlineData(KitVerifier.SignatureFileName)]
    public void Missing_manifest_or_signature_is_rejected_with_code_20(string fileName)
    {
        File.Delete(Path.Combine(kitRoot, fileName));

        Assert.Equal(ExitCodes.InvalidKitSignature, Verify().ExitCode);
    }

    [Fact]
    public void Malformed_signature_is_rejected_with_code_20()
    {
        File.WriteAllText(Path.Combine(kitRoot, KitVerifier.SignatureFileName), "not base64!");

        Assert.Equal(ExitCodes.InvalidKitSignature, Verify().ExitCode);
    }

    [Fact]
    public void Installer_without_embedded_public_key_rejects_the_kit_with_code_20()
    {
        var result = new KitVerifier(null).Verify(kitRoot);

        Assert.Equal(ExitCodes.InvalidKitSignature, result.ExitCode);
    }

    [Fact]
    public void Missing_kit_file_is_rejected_with_code_21()
    {
        File.Delete(Path.Combine(kitRoot, "templates", "answers.example.json"));

        var result = Verify();

        Assert.Equal(ExitCodes.KitFilesMissingOrModified, result.ExitCode);
        Assert.Contains("lipsă: templates/answers.example.json", result.Details);
    }

    [Fact]
    public void Extra_kit_file_is_rejected_with_code_21()
    {
        File.WriteAllText(Path.Combine(kitRoot, "database", "migrations", "core", "2026.11.002_Injected.sql"), "DROP TABLE x;");

        var result = Verify();

        Assert.Equal(ExitCodes.KitFilesMissingOrModified, result.ExitCode);
        Assert.Contains("în plus: database/migrations/core/2026.11.002_Injected.sql", result.Details);
    }

    [Fact]
    public void Unlisted_setup_executable_is_left_to_authenticode()
    {
        manifestFiles.RemoveAll(f => f.Path == "ErpSetup.exe");
        WriteSignedManifest();

        Assert.True(Verify().Succeeded);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("app\\host\\Erp.Host.dll")]
    [InlineData("/app/host/Erp.Host.dll")]
    [InlineData("C:/Windows/win.ini")]
    public void Unsafe_manifest_paths_are_rejected_with_code_21(string path)
    {
        manifestFiles.Add(new KitFile(path, new string('0', 64), 0));
        WriteSignedManifest();

        var result = Verify();

        Assert.Equal(ExitCodes.KitFilesMissingOrModified, result.ExitCode);
        Assert.Contains($"intrare invalidă în manifest: {path}", result.Details);
    }

    [Fact]
    public void Kit_for_another_product_is_rejected_with_code_21()
    {
        WriteSignedManifest(product: "OTHER");

        Assert.Equal(ExitCodes.KitFilesMissingOrModified, Verify().ExitCode);
    }

    private readonly List<KitFile> manifestFiles = [];

    private void AddFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(kitRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content, new UTF8Encoding(false));
        manifestFiles.Add(new KitFile(relativePath, KitVerifier.ComputeSha256(fullPath), new FileInfo(fullPath).Length));
    }

    private void FlipFirstByte(string relativePath)
    {
        var fullPath = Path.Combine(kitRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var bytes = File.ReadAllBytes(fullPath);
        bytes[0] ^= 0x01;
        File.WriteAllBytes(fullPath, bytes);
    }

    private void WriteSignedManifest(string product = "ERP")
    {
        var manifest = new KitManifest
        {
            Product = product,
            Version = "2027.1.0",
            ReleaseDate = "2027-01-20",
            MinUpgradeFrom = "2026.2.0",
            MinSqlServerMajorVersion = 16,
            SqlCompatibilityLevel = 160,
            DatabaseCollation = "Latin1_General_100_CS_AS_SC_UTF8",
            HostingBundleVersion = "10.0.3",
            LastMigration = "core/2026.11.001_Create_Company.sql",
            Files = manifestFiles,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, KitManifest.JsonOptions);
        File.WriteAllBytes(Path.Combine(kitRoot, KitVerifier.ManifestFileName), bytes);
        var signature = signingKey.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        File.WriteAllText(Path.Combine(kitRoot, KitVerifier.SignatureFileName), Convert.ToBase64String(signature));
    }
}
