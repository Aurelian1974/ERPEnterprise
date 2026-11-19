using System.Buffers.Binary;
using System.Security.Cryptography;
using Erp.Licensing;

namespace Erp.LicenseGen.Core;

/// <summary>Scrierea fisierului .lic (08, 3.4): cripteaza AES-256-GCM, apoi semneaza ECDSA P-256 tot ce precede semnatura.</summary>
public static class LicenseWriter
{
    public static byte[] Write(License license, LicenseSigningKey key)
    {
        if (license.Validate() is { } problem)
        {
            throw new ArgumentException($"Licența nu respectă regulile: {problem}.", nameof(license));
        }

        var json = LicenseFormat.Serialize(license);
        try
        {
            if (json.Length > LicenseFormat.MaxCiphertextLength)
            {
                throw new ArgumentException("Licența este prea mare.", nameof(license));
            }

            var file = new byte[LicenseFormat.TotalLength(json.Length)];
            var span = file.AsSpan();
            LicenseFormat.Marker.CopyTo(span);
            span[LicenseFormat.VersionOffset] = LicenseFormat.Version;
            span[LicenseFormat.KeyIdOffset] = key.KeyId;
            RandomNumberGenerator.Fill(span.Slice(LicenseFormat.NonceOffset, LicenseFormat.NonceLength));
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(LicenseFormat.LengthOffset, 4), (uint)json.Length);

            using (var aes = new AesGcm(key.AesKey, LicenseFormat.TagLength))
            {
                aes.Encrypt(
                    span.Slice(LicenseFormat.NonceOffset, LicenseFormat.NonceLength),
                    json,
                    span.Slice(LicenseFormat.HeaderLength, json.Length),
                    span.Slice(LicenseFormat.HeaderLength + json.Length, LicenseFormat.TagLength),
                    span[..LicenseFormat.HeaderLength]);
            }

            var signedLength = LicenseFormat.HeaderLength + json.Length + LicenseFormat.TagLength;
            key.Sign(span[..signedLength]).CopyTo(span[signedLength..]);
            return file;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }
}
