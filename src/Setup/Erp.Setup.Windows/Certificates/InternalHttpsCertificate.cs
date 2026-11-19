using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Erp.Setup.Windows.Certificates;

/// <summary>
/// Certificatul HTTPS intern (I-04, 5.11, D9): RSA 3072, SAN = adresa aleasa si numele scurt al calculatorului, valabil 2 ani
/// (sub limita de 825 de zile), EKU Server Authentication; in LocalMachine\My, copiat in LocalMachine\Root pe acest calculator,
/// iar partea publica se exporta in certificat-erp.cer pentru calculatoarele utilizatorilor.
/// </summary>
public static class InternalHttpsCertificate
{
    public const string ServerAuthenticationEku = "1.3.6.1.5.5.7.3.1";
    public const int ValidityYears = 2;

    public static X509Certificate2 Create(string hostName, string machineName, DateTimeOffset now)
    {
        using var key = RSA.Create(3072);
        var request = new CertificateRequest($"CN={hostName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(hostName);
        if (!string.Equals(hostName, machineName, StringComparison.OrdinalIgnoreCase))
        {
            san.AddDnsName(machineName);
        }

        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthenticationEku)], critical: false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(ValidityYears));
    }

    /// <summary>Creeaza certificatul in LocalMachine\My (cheie persistenta), il adauga in Root, scrie .cer si intoarce amprenta.</summary>
    public static string CreateInMachineStore(string instance, string hostName, string machineName, string publicCertificatePath, DateTimeOffset now)
    {
        using var ephemeral = Create(hostName, machineName, now);
        using var persisted = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
        persisted.FriendlyName = $"ERP-{instance} HTTPS ({hostName})";

        using (var my = new X509Store(StoreName.My, StoreLocation.LocalMachine))
        {
            my.Open(OpenFlags.ReadWrite);
            my.Add(persisted);
        }

        using var publicOnly = X509CertificateLoader.LoadCertificate(persisted.RawData);
        using (var root = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
        {
            root.Open(OpenFlags.ReadWrite);
            root.Add(publicOnly);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(publicCertificatePath)!);
        File.WriteAllBytes(publicCertificatePath, publicOnly.Export(X509ContentType.Cert));
        return persisted.Thumbprint;
    }
}
