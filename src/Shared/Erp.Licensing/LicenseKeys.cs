using System.Security.Cryptography;

namespace Erp.Licensing;

/// <summary>Cheile unei perechi (08, 3.5): cheia publica ECDSA (SubjectPublicKeyInfo) si cheia AES-256.</summary>
public sealed class LicenseKey
{
    public LicenseKey(byte keyId, byte[] publicKey, byte[] aesKey)
    {
        if (keyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keyId), "KeyId este 1–255.");
        }

        if (aesKey.Length != LicenseFormat.AesKeyLength)
        {
            throw new ArgumentException("Cheia AES trebuie să aibă 256 de biți.", nameof(aesKey));
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        if (ecdsa.KeySize != 256)
        {
            throw new ArgumentException("Cheia publică trebuie să fie ECDSA P-256.", nameof(publicKey));
        }

        KeyId = keyId;
        PublicKey = publicKey;
        AesKey = aesKey;
    }

    public byte KeyId { get; }

    public byte[] PublicKey { get; }

    public byte[] AesKey { get; }
}

/// <summary>Cheile acceptate de o versiune, pe KeyId (activa si de rezerva, decizia D16).</summary>
public sealed class LicenseKeyRing
{
    private readonly Dictionary<byte, LicenseKey> keys;

    public LicenseKeyRing(IEnumerable<LicenseKey> keys) => this.keys = keys.ToDictionary(k => k.KeyId);

    public IReadOnlyCollection<byte> KeyIds => keys.Keys;

    public LicenseKey? Find(byte keyId) => keys.GetValueOrDefault(keyId);
}

/// <summary>
/// Cheile compilate in aceasta versiune (08, 3.5). Intrarile se lipesc din textul afisat de ErpLicenseGen › Chei:
/// cheia publica ECDSA si cheia AES mascata (XOR cu o a doua valoare), ca sa nu apara in clar ca sir in binar.
/// Mascarea e ascundere, nu secret: protectia reala e semnatura (08, 3.4). Gol pana la generarea cheilor de productie,
/// deci orice licenta reala e refuzata cu „nu este emisa de producator”.
/// </summary>
public static class LicenseKeys
{
    private static readonly EmbeddedKey[] Entries =
    [
        // ErpLicenseGen › Chei: new(KeyId, "<cheia publica, Base64>", "<cheia AES mascata, Base64>", "<masca, Base64>"),
    ];

    public static LicenseKeyRing Embedded { get; } = new(Entries.Select(e => e.ToKey()));

    /// <summary>Linia C# pentru <see cref="Entries"/>, scrisa de generator la configurarea cheilor.</summary>
    public static string ToSourceLine(byte keyId, byte[] publicKey, byte[] aesKey)
    {
        var mask = RandomNumberGenerator.GetBytes(aesKey.Length);
        var masked = Xor(aesKey, mask);
        return $"new({keyId}, \"{Convert.ToBase64String(publicKey)}\", \"{Convert.ToBase64String(masked)}\", \"{Convert.ToBase64String(mask)}\"),";
    }

    internal static byte[] Xor(byte[] value, byte[] mask)
    {
        var result = new byte[value.Length];
        for (var i = 0; i < value.Length; i++)
        {
            result[i] = (byte)(value[i] ^ mask[i]);
        }

        return result;
    }

    private readonly record struct EmbeddedKey(byte KeyId, string PublicKey, string AesMasked, string AesMask)
    {
        public LicenseKey ToKey() => new(KeyId, Convert.FromBase64String(PublicKey),
            Xor(Convert.FromBase64String(AesMasked), Convert.FromBase64String(AesMask)));
    }
}
