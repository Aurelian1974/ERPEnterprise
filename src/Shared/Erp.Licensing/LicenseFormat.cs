using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erp.Licensing;

/// <summary>
/// Formatul binar al fisierului .lic (08, 3.4): marcaj, versiune, KeyId, nonce, lungime, text criptat AES-256-GCM, tag,
/// semnatura ECDSA P-256 (IEEE P1363) peste tot ce o precede. Antetul (octetii 0…25) e AAD pentru GCM.
/// </summary>
public static class LicenseFormat
{
    public const int Version = 1;
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int SignatureLength = 64;
    public const int HeaderLength = 26;
    public const int MaxCiphertextLength = 65_536;
    public const int AesKeyLength = 32;

    public static ReadOnlySpan<byte> Marker => "ERPLIC01"u8;

    internal const int VersionOffset = 8;
    internal const int KeyIdOffset = 9;
    internal const int NonceOffset = 10;
    internal const int LengthOffset = 22;

    /// <summary>Lungimea totala a unui fisier cu textul criptat de n octeti.</summary>
    public static int TotalLength(int ciphertextLength) => HeaderLength + ciphertextLength + TagLength + SignatureLength;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>JSON-ul UTF-8 al licentei (08, 3.3), exact cum il scrie generatorul.</summary>
    public static byte[] Serialize(License license) => JsonSerializer.SerializeToUtf8Bytes(license, JsonOptions);

    internal static License? Deserialize(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<License>(json, JsonOptions);
}
