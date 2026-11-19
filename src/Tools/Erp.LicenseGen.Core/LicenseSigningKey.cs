using System.Security.Cryptography;
using Erp.Licensing;

namespace Erp.LicenseGen.Core;

/// <summary>
/// O pereche de chei de emitere (08, 3.5): cheia privata ECDSA P-256 si cheia AES-256 cu acelasi KeyId.
/// Exista doar in generator; nu intra niciodata in Git sau in ERP (R7, SEC-02).
/// </summary>
public sealed class LicenseSigningKey : IDisposable
{
    private readonly ECDsa signer;

    private LicenseSigningKey(byte keyId, ECDsa signer, byte[] aesKey)
    {
        if (keyId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(keyId), "KeyId este 1–255.");
        }

        if (signer.KeySize != 256 || aesKey.Length != LicenseFormat.AesKeyLength)
        {
            throw new ArgumentException("Cheile trebuie să fie ECDSA P-256 și AES-256.");
        }

        KeyId = keyId;
        this.signer = signer;
        AesKey = aesKey;
    }

    public byte KeyId { get; }

    public byte[] AesKey { get; }

    public byte[] PublicKey => signer.ExportSubjectPublicKeyInfo();

    public static LicenseSigningKey Generate(byte keyId) =>
        new(keyId, ECDsa.Create(ECCurve.NamedCurves.nistP256), RandomNumberGenerator.GetBytes(LicenseFormat.AesKeyLength));

    /// <summary>Din cheia privata PKCS#8 (necriptata, deja decriptata de apelant) si cheia AES.</summary>
    public static LicenseSigningKey Import(byte keyId, ReadOnlySpan<byte> pkcs8PrivateKey, byte[] aesKey)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(pkcs8PrivateKey, out _);
        return new LicenseSigningKey(keyId, ecdsa, aesKey);
    }

    /// <summary>Cheia publica, asa cum o accepta ERP-ul (pentru teste si pentru „Deschide o licenta”).</summary>
    public LicenseKey ToPublicKey() => new(KeyId, PublicKey, AesKey);

    /// <summary>Linia pentru LicenseKeys.cs (08, 3.5).</summary>
    public string ToSourceLine() => LicenseKeys.ToSourceLine(KeyId, PublicKey, AesKey);

    public byte[] ExportPkcs8PrivateKey() => signer.ExportPkcs8PrivateKey();

    internal byte[] Sign(ReadOnlySpan<byte> data) =>
        signer.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public void Dispose()
    {
        signer.Dispose();
        CryptographicOperations.ZeroMemory(AesKey);
    }
}
