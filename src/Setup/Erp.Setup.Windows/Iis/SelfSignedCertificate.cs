using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Erp.Setup.Windows.Iis;

/// <summary>
/// Certificatul autosemnat (6.2, allowSelfSignedCertificate = true fara amprenta): valabil 1 an, CN si SAN = hostName,
/// EKU Server Authentication, cheie RSA 2048 in LocalMachine\My. Doar pentru test.
/// </summary>
public static class SelfSignedCertificate
{
    public const string ServerAuthenticationEku = "1.3.6.1.5.5.7.3.1";

    public static X509Certificate2 Create(string hostName, DateTimeOffset now)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={hostName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(hostName);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationEku)], critical: false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));
    }

    /// <summary>Creeaza certificatul cu cheie persistenta in LocalMachine\My si intoarce amprenta.</summary>
    public static string CreateInMachineStore(string hostName, DateTimeOffset now)
    {
        using var ephemeral = Create(hostName, now);
        using var persisted = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        persisted.FriendlyName = $"ERP {hostName} (autosemnat)";
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        store.Add(persisted);
        return persisted.Thumbprint;
    }
}
