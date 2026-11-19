using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Erp.Licensing;

/// <summary>Rezultatul verificarilor 1–5 (08, 4.2), in ordinea lor; primul esec opreste verificarea.</summary>
public enum LicenseStatus
{
    Valid,

    /// <summary>1: marcajul, versiunea sau lungimile.</summary>
    NotALicense,

    /// <summary>2: KeyId necunoscut sau semnatura invalida.</summary>
    NotIssuedByVendor,

    /// <summary>3–4: decriptarea sau continutul.</summary>
    Damaged,

    /// <summary>5: licenta e autentica, dar a expirat fata de data de referinta.</summary>
    Expired,
}

public sealed record LicenseReadResult(LicenseStatus Status, License? License, byte KeyId, IReadOnlyList<string> UnknownModules)
{
    public bool IsValid => Status == LicenseStatus.Valid;

    /// <summary>Autentica si intreaga (valida sau expirata): aplicatia o foloseste si pentru perioada de gratie (08, 4.4).</summary>
    public bool IsAuthentic => Status is LicenseStatus.Valid or LicenseStatus.Expired;

    /// <summary>Mesajul pentru utilizator (08, 4.2); nu contine continutul licentei.</summary>
    public string Message => Status switch
    {
        LicenseStatus.Valid => $"Licența {License!.LicenseId} ({License.CustomerName}) este validă.",
        LicenseStatus.NotALicense => "Fișierul nu este o licență ERP.",
        LicenseStatus.NotIssuedByVendor => "Licența nu este emisă de producător sau a fost modificată.",
        LicenseStatus.Damaged => "Licența este deteriorată.",
        LicenseStatus.Expired => $"Licența a expirat la {License!.ValidUntil!.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}. Cereți furnizorului o licență nouă.",
        _ => Status.ToString(),
    };
}

/// <summary>Verificarea fisierului de licenta (08, 4.2). Aceeasi in generator, installer si aplicatie.</summary>
public interface ILicenseReader
{
    /// <param name="referenceDate">Data fata de care se verifica expirarea (08, 4.4); implicit data curenta.</param>
    LicenseReadResult Read(ReadOnlySpan<byte> file, DateOnly? referenceDate = null);
}

public sealed class LicenseReader(LicenseKeyRing keys, TimeProvider time) : ILicenseReader
{
    public LicenseReadResult Read(ReadOnlySpan<byte> file, DateOnly? referenceDate = null)
    {
        // 1. Marcajul, versiunea, lungimile.
        if (file.Length < LicenseFormat.TotalLength(0)
            || !file[..LicenseFormat.Marker.Length].SequenceEqual(LicenseFormat.Marker)
            || file[LicenseFormat.VersionOffset] != LicenseFormat.Version)
        {
            return Fail(LicenseStatus.NotALicense);
        }

        var keyId = file[LicenseFormat.KeyIdOffset];
        var length = BinaryPrimitives.ReadUInt32LittleEndian(file.Slice(LicenseFormat.LengthOffset, 4));
        if (length > LicenseFormat.MaxCiphertextLength || file.Length != LicenseFormat.TotalLength((int)length))
        {
            return Fail(LicenseStatus.NotALicense, keyId);
        }

        var n = (int)length;
        var signed = file[..(LicenseFormat.HeaderLength + n + LicenseFormat.TagLength)];
        var signature = file[signed.Length..];

        // 2. KeyId cunoscut si semnatura valida, inainte de orice decriptare.
        var key = keys.Find(keyId);
        if (key is null)
        {
            return Fail(LicenseStatus.NotIssuedByVendor, keyId);
        }

        using (var ecdsa = ECDsa.Create())
        {
            ecdsa.ImportSubjectPublicKeyInfo(key.PublicKey, out _);
            if (!ecdsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return Fail(LicenseStatus.NotIssuedByVendor, keyId);
            }
        }

        // 3. Decriptarea AES-GCM, cu antetul ca date autentificate.
        var plaintext = new byte[n];
        try
        {
            using var aes = new AesGcm(key.AesKey, LicenseFormat.TagLength);
            aes.Decrypt(
                file.Slice(LicenseFormat.NonceOffset, LicenseFormat.NonceLength),
                file.Slice(LicenseFormat.HeaderLength, n),
                file.Slice(LicenseFormat.HeaderLength + n, LicenseFormat.TagLength),
                plaintext,
                file[..LicenseFormat.HeaderLength]);
        }
        catch (CryptographicException)
        {
            return Fail(LicenseStatus.Damaged, keyId);
        }

        // 4. JSON valid si regulile de continut.
        License? license;
        try
        {
            license = LicenseFormat.Deserialize(plaintext);
        }
        catch (JsonException)
        {
            return Fail(LicenseStatus.Damaged, keyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        if (license is null || license.Validate() is not null)
        {
            return Fail(LicenseStatus.Damaged, keyId);
        }

        var unknown = license.Modules.Where(m => !LicenseModules.Known.Contains(m)).ToList();

        // 5. Expirarea fata de data de referinta.
        var reference = referenceDate ?? DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var status = license.ValidUntil is { } until && until < reference ? LicenseStatus.Expired : LicenseStatus.Valid;
        return new LicenseReadResult(status, license, keyId, unknown);
    }

    private static LicenseReadResult Fail(LicenseStatus status, byte keyId = 0) => new(status, null, keyId, []);
}
