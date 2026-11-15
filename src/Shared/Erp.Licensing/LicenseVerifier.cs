using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erp.Licensing;

/// <summary>Continutul semnat al licentei (8.9).</summary>
public sealed record LicensePayload
{
    public required string LicenseId { get; init; }

    public required string Product { get; init; }

    public required string CustomerName { get; init; }

    public required IReadOnlyList<string> FiscalCodes { get; init; }

    public required IReadOnlyList<string> Modules { get; init; }

    public required LicenseLimits Limits { get; init; }

    public required DateOnly IssuedAt { get; init; }

    public required DateOnly ValidUntil { get; init; }
}

public sealed record LicenseLimits(int Companies, int Users, int PayrollEmployees, int PosTerminals);

public enum LicenseStatus
{
    Valid,
    InvalidFormat,
    UnknownKey,
    InvalidSignature,
    WrongProduct,
    Expired,
}

public sealed record LicenseVerificationResult(
    LicenseStatus Status,
    LicensePayload? Payload,
    IReadOnlyList<string> UnknownModules,
    string? KeyId)
{
    public bool IsValid => Status == LicenseStatus.Valid;

    /// <summary>Mesajul pentru operator, in romana; nu contine payload-ul sau semnatura.</summary>
    public string Message => Status switch
    {
        LicenseStatus.Valid => $"Licența {Payload!.LicenseId} ({Payload.CustomerName}), valabilă până la {Payload.ValidUntil:yyyy-MM-dd}.",
        LicenseStatus.InvalidFormat => "Fișierul de licență nu are formatul așteptat.",
        LicenseStatus.UnknownKey => $"Licența este semnată cu o cheie necunoscută ({KeyId}).",
        LicenseStatus.InvalidSignature => "Semnătura licenței este invalidă.",
        LicenseStatus.WrongProduct => $"Licența este pentru alt produs ({Payload?.Product}).",
        LicenseStatus.Expired => $"Licența a expirat la {Payload!.ValidUntil:yyyy-MM-dd}.",
        _ => Status.ToString(),
    };
}

/// <summary>
/// Verificarea licentei (8.9): semnatura ECDSA P-256 / SHA-256 (format IEEE P1363) peste octetii payload-ului,
/// cu cheia publica aleasa dupa keyId; produsul exact „ERP”; validUntil &gt;= azi; module necunoscute = avertisment.
/// Folosita identic de installer si de Erp.Host.
/// </summary>
public sealed class LicenseVerifier(IReadOnlyDictionary<string, string> publicKeysByKeyId, TimeProvider time)
{
    public const string Product = "ERP";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public LicenseVerificationResult Verify(string licenseFileContent)
    {
        LicenseEnvelope? envelope;
        byte[] payloadBytes;
        byte[] signature;
        try
        {
            envelope = JsonSerializer.Deserialize<LicenseEnvelope>(licenseFileContent, JsonOptions);
            if (envelope is null)
            {
                return Fail(LicenseStatus.InvalidFormat);
            }

            payloadBytes = FromBase64Url(envelope.Payload);
            signature = FromBase64Url(envelope.Signature);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return Fail(LicenseStatus.InvalidFormat);
        }

        if (!publicKeysByKeyId.TryGetValue(envelope.KeyId, out var pem))
        {
            return Fail(LicenseStatus.UnknownKey, keyId: envelope.KeyId);
        }

        using (var key = ECDsa.Create())
        {
            key.ImportFromPem(pem);
            if (key.KeySize != 256
                || !key.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return Fail(LicenseStatus.InvalidSignature, keyId: envelope.KeyId);
            }
        }

        LicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return Fail(LicenseStatus.InvalidFormat, keyId: envelope.KeyId);
        }

        if (payload is null)
        {
            return Fail(LicenseStatus.InvalidFormat, keyId: envelope.KeyId);
        }

        if (!string.Equals(payload.Product, Product, StringComparison.Ordinal))
        {
            return Fail(LicenseStatus.WrongProduct, payload, envelope.KeyId);
        }

        if (payload.ValidUntil < DateOnly.FromDateTime(time.GetLocalNow().DateTime))
        {
            return Fail(LicenseStatus.Expired, payload, envelope.KeyId);
        }

        var unknown = payload.Modules.Where(m => !LicenseModules.Known.Contains(m)).ToList();
        return new LicenseVerificationResult(LicenseStatus.Valid, payload, unknown, envelope.KeyId);
    }

    private static LicenseVerificationResult Fail(LicenseStatus status, LicensePayload? payload = null, string? keyId = null) =>
        new(status, payload, [], keyId);

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        return Convert.FromBase64String(base64);
    }

    private sealed record LicenseEnvelope(string Payload, string Signature, string KeyId);
}

/// <summary>Codurile de modul cunoscute de aceasta versiune (comparatie exacta, NET-21).</summary>
public static class LicenseModules
{
    public static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal)
    {
        "Core", "Finance", "Sales", "Purchasing", "Inventory", "Integrations", "Wms", "Tms",
        "Manufacturing", "Hr", "Pos", "Construction", "Crm", "Reporting",
    };
}

/// <summary>Cheile publice incorporate (Keys/&lt;keyId&gt;.pem).</summary>
public static class LicenseKeys
{
    private const string Prefix = "Erp.Licensing.Keys.";

    public static IReadOnlyDictionary<string, string> LoadEmbedded()
    {
        var assembly = typeof(LicenseKeys).Assembly;
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".pem", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            keys[name[Prefix.Length..^4]] = reader.ReadToEnd();
        }

        return keys;
    }
}
