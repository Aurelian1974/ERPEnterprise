using Erp.Setup.Core;
using Erp.Setup.Core.Kit;

namespace Erp.Setup.Tests.Kit;

/// <summary>
/// Verifica un kit generat de build/package-kit.ps1 cu verificatorul installer-ului (compatibilitatea formatului).
/// Ruleaza cand sunt setate ERP_TEST_KIT_DIR si ERP_TEST_KIT_PUBLIC_KEY (in CI, dupa generarea kitului de test).
/// </summary>
public sealed class PackagedKitTests : IDisposable
{
    private static readonly string? KitDirectory = Environment.GetEnvironmentVariable("ERP_TEST_KIT_DIR");
    private static readonly string? PublicKeyPath = Environment.GetEnvironmentVariable("ERP_TEST_KIT_PUBLIC_KEY");

    private readonly string copy = Path.Combine(Path.GetTempPath(), "erp-packaged-kit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    private static KitVerifier Verifier() => new(File.ReadAllText(PublicKeyPath!));

    private static void SkipWithoutKit() =>
        Assert.SkipWhen(string.IsNullOrEmpty(KitDirectory) || string.IsNullOrEmpty(PublicKeyPath),
            "ERP_TEST_KIT_DIR și ERP_TEST_KIT_PUBLIC_KEY nu sunt setate.");

    [Fact]
    [Trait("Criteriu", "08-IA-13")]
    public void Packaged_kit_is_accepted_by_the_installer_verifier()
    {
        SkipWithoutKit();

        var result = Verifier().Verify(KitDirectory!);

        Assert.True(result.Succeeded, string.Join("; ", result.Details));
        Assert.Contains(result.Manifest!.Files, f => f.Path == "app/host/Erp.Host.dll");
        Assert.Contains(result.Manifest.Files, f => f.Path == "ErpSetup.exe");
        Assert.Contains(result.Manifest.Files, f => f.Path == "ErpSetup.Cli.exe");
        Assert.Contains(result.Manifest.Files, f => f.Path == "templates/app_offline.template.htm");
        Assert.Equal(1, result.Manifest.KitKeyId);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-13")]
    public void Packaged_kit_with_one_modified_byte_is_rejected()
    {
        SkipWithoutKit();
        CopyKit();
        var target = Path.Combine(copy, "app", "host", "Erp.Host.dll");
        var bytes = File.ReadAllBytes(target);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(target, bytes);

        Assert.Equal(ExitCodes.KitFilesMissingOrModified, Verifier().Verify(copy).ExitCode);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-13")]
    public void Packaged_kit_with_modified_manifest_is_rejected()
    {
        SkipWithoutKit();
        CopyKit();
        var manifest = Path.Combine(copy, KitVerifier.ManifestFileName);
        File.AppendAllText(manifest, " ");

        Assert.Equal(ExitCodes.InvalidKitSignature, Verifier().Verify(copy).ExitCode);
    }

    [Fact]
    [Trait("Criteriu", "08-IA-18")]
    public void Packaged_binaries_embed_the_manifest_version_and_last_migration()
    {
        SkipWithoutKit();
        var manifest = Verifier().Verify(KitDirectory!).Manifest!;

        foreach (var path in new[] { "app/host/Erp.Host.dll", "app/worker/Erp.Worker.dll" })
        {
            var context = new System.Runtime.Loader.AssemblyLoadContext(path, isCollectible: true);
            try
            {
                var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(Path.Combine(KitDirectory!, path)));
                var metadata = assembly.GetCustomAttributesData()
                    .Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute")
                    .ToDictionary(a => (string)a.ConstructorArguments[0].Value!, a => (string?)a.ConstructorArguments[1].Value);

                Assert.Equal(manifest.Version, metadata["ErpVersion"]);
                Assert.Equal(manifest.LastMigration, metadata["ErpLastMigration"]);
            }
            finally
            {
                context.Unload();
            }
        }
    }

    private void CopyKit()
    {
        foreach (var file in Directory.EnumerateFiles(KitDirectory!, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(copy, Path.GetRelativePath(KitDirectory!, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
}
