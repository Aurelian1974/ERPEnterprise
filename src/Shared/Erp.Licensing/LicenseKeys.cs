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

        // Copii proprii: apelantul poate sterge cheia lui (de exemplu LicenseSigningKey.Dispose) dupa ce a construit-o pe aceasta.
        KeyId = keyId;
        PublicKey = (byte[])publicKey.Clone();
        AesKey = (byte[])aesKey.Clone();
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
        new(1, "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEd/oaommCxObNNIBql47l8MT8jEkgr3LJmp6TRphM5NaHc5ElBIqHsuAhRVf4G6nEIrI2b8UFujD+YKrd+ZGzzw==", "0FyhT0QAs56dlfYLga3R9maxWlJ8vM+iOdrYZNbprB4=", "GrwT1e+U2ksecBJkwiz323dPZeIfAB9ln7W1H7XMge8="),
        new(2, "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEeS0QasyWzJEBGGLWrqiFL8NEyBpCTpV5tzLnoXrlyXHDZ3Lkee9bSumVq5jquFveXdG2zQ/pRi9rlWOkgUHlGA==", "6J+7ZYrB5Ps/ckXvZGvclf3z3vD/H/2Oq26c5mT8+qw=", "n9yrqXheg0zPGS99svZF8JOE/14H36ErWqfiWQ+jsZg="),
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
