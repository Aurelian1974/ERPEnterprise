using System.Security.Cryptography;
using System.Text.Json;

namespace Erp.Setup.Core.Kit;

/// <summary>
/// Verificarea kitului (4.3): semnatura ECDSA P-256/SHA-256 a manifest.json (cod 20),
/// apoi SHA-256 si dimensiunea fiecarui fisier, fara fisiere lipsa sau in plus (cod 21).
/// manifest.sig contine semnatura IEEE P1363 (r||s, 64 de octeti) codata Base64.
/// </summary>
public sealed class KitVerifier
{
    public const string ManifestFileName = "manifest.json";
    public const string SignatureFileName = "manifest.sig";
    public const string SetupExecutableName = "ErpSetup.exe";
    public const string Product = "ERP";

    public const string SignatureFailureMessage = "Kitul nu este semnat de producător sau a fost modificat.";
    public const string FilesFailureMessage = "Fișiere din kit lipsă sau modificate.";

    private readonly string? publicKeyPem;

    public KitVerifier(string? publicKeyPem)
    {
        this.publicKeyPem = publicKeyPem;
    }

    public KitVerificationResult Verify(string kitRoot)
    {
        var manifestPath = Path.Combine(kitRoot, ManifestFileName);
        var signaturePath = Path.Combine(kitRoot, SignatureFileName);

        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return KitVerificationResult.Fail(ExitCodes.InvalidKitSignature, SignatureFailureMessage,
                "Installer-ul nu conține cheia publică a kitului.");
        }

        if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
        {
            return KitVerificationResult.Fail(ExitCodes.InvalidKitSignature, SignatureFailureMessage,
                $"Lipsește {ManifestFileName} sau {SignatureFileName}.");
        }

        var manifestBytes = File.ReadAllBytes(manifestPath);
        if (!IsSignatureValid(manifestBytes, File.ReadAllText(signaturePath)))
        {
            return KitVerificationResult.Fail(ExitCodes.InvalidKitSignature, SignatureFailureMessage);
        }

        KitManifest manifest;
        try
        {
            manifest = KitManifest.Parse(manifestBytes);
        }
        catch (JsonException)
        {
            return KitVerificationResult.Fail(ExitCodes.KitFilesMissingOrModified, FilesFailureMessage,
                "Manifestul kitului nu poate fi citit.");
        }

        if (!string.Equals(manifest.Product, Product, StringComparison.Ordinal))
        {
            return KitVerificationResult.Fail(ExitCodes.KitFilesMissingOrModified, FilesFailureMessage,
                $"Kitul este pentru produsul „{manifest.Product}”, nu {Product}.");
        }

        var problems = VerifyFiles(kitRoot, manifest.Files);
        return problems.Count == 0
            ? KitVerificationResult.Ok(manifest)
            : KitVerificationResult.Fail(ExitCodes.KitFilesMissingOrModified, FilesFailureMessage, [.. problems]);
    }

    private bool IsSignatureValid(byte[] manifestBytes, string signatureText)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureText.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        using var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(publicKeyPem);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
        {
            return false;
        }

        return key.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static List<string> VerifyFiles(string kitRoot, IReadOnlyList<KitFile> files)
    {
        var problems = new List<string>();
        var listed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (!IsSafeRelativePath(file.Path) || !listed.Add(file.Path))
            {
                problems.Add($"intrare invalidă în manifest: {file.Path}");
                continue;
            }

            var fullPath = Path.Combine(kitRoot, file.Path.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                problems.Add($"lipsă: {file.Path}");
            }
            else if (info.Length != file.Size || !string.Equals(ComputeSha256(fullPath), file.Sha256, StringComparison.Ordinal))
            {
                problems.Add($"modificat: {file.Path}");
            }
        }

        foreach (var fullPath in Directory.EnumerateFiles(kitRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(kitRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            if (relative is ManifestFileName or SignatureFileName or SetupExecutableName)
            {
                continue;
            }

            if (!listed.Contains(relative))
            {
                problems.Add($"în plus: {relative}");
            }
        }

        return problems;
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains(':') || path.StartsWith('/'))
        {
            return false;
        }

        return path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not ".."
            && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }
}

public sealed record KitVerificationResult(int ExitCode, string? Message, IReadOnlyList<string> Details, KitManifest? Manifest)
{
    public bool Succeeded => ExitCode == ExitCodes.Success;

    public static KitVerificationResult Ok(KitManifest manifest) => new(ExitCodes.Success, null, [], manifest);

    public static KitVerificationResult Fail(int exitCode, string message, params string[] details) =>
        new(exitCode, message, details, null);
}
