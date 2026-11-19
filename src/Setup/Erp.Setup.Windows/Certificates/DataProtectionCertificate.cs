using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Erp.Setup.Core;
using Erp.Setup.Windows.Services;

namespace Erp.Setup.Windows.Certificates;

/// <summary>
/// Certificatul de protectie a datelor (I-08, 5.12, D22): CN=ERP-&lt;Instanta&gt; Data Protection, RSA 3072, valabil 20 de ani,
/// Key Usage = Key Encipherment, cheia privata exportabila in LocalMachine\My, cu drept de citire pentru identitatile aplicatiei.
/// Cheia de recuperare = exportul lui PKCS#12 cu AES-256; parola nu se stocheaza nicaieri. „Repară” nu il recreeaza niciodata.
/// </summary>
public static class DataProtectionCertificate
{
    public const int ValidityYears = 20;

    /// <summary>PBES2 cu AES-256-CBC si SHA-256 (importabil pe Windows 10 1709+ si Windows Server 2019+).</summary>
    private static readonly PbeParameters RecoveryKeyEncryption = new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000);

    public const int MinimumPasswordLength = RecoveryKeyPolicy.MinimumPasswordLength;

    public static string Subject(string instance) => $"CN=ERP-{instance} Data Protection";

    public static X509Certificate2 Create(string instance, DateTimeOffset now)
    {
        using var key = RSA.Create(3072);
        var request = new CertificateRequest(Subject(instance), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, critical: true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        return request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(ValidityYears));
    }

    /// <summary>Creeaza certificatul in LocalMachine\My si intoarce amprenta.</summary>
    public static string CreateInMachineStore(string instance, DateTimeOffset now)
    {
        using var ephemeral = Create(instance, now);
        return AddToMachineStore(ephemeral.Export(X509ContentType.Pkcs12), password: null, instance);
    }

    /// <summary>„Recuperează” (11.2): importa cheia de recuperare in LocalMachine\My; intoarce amprenta.</summary>
    public static string Import(string pfxPath, string password, string instance)
    {
        if (Check(pfxPath, password, expectedThumbprint: null) is var check and not RecoveryKeyCheck.Valid)
        {
            throw new RecoveryKeyException(check);
        }

        return AddToMachineStore(File.ReadAllBytes(pfxPath), password, instance);
    }

    public static X509Certificate2? Find(string thumbprint)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>Exporta cheia de recuperare (ecranul 6, 11.4) intr-un fisier .pfx protejat cu AES-256.</summary>
    public static void Export(string thumbprint, string pfxPath, string password)
    {
        if (password.Length < MinimumPasswordLength)
        {
            throw new ArgumentException($"Parola cheii de recuperare are cel puțin {MinimumPasswordLength} caractere.", nameof(password));
        }

        using var certificate = Find(thumbprint)
            ?? throw new WindowsSetupException($"Certificatul de protecție a datelor ({thumbprint}) lipsește din LocalMachine\\My.");
        var bytes = certificate.ExportPkcs12(RecoveryKeyEncryption, password);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pfxPath))!);
        var temporary = pfxPath + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, pfxPath, overwrite: true);

        // Exportul se verifica imediat: o cheie de recuperare care nu se poate deschide nu protejeaza nimic.
        if (Check(pfxPath, password, thumbprint) != RecoveryKeyCheck.Valid)
        {
            throw new WindowsSetupException("Cheia de recuperare exportată nu a putut fi verificată.");
        }
    }

    /// <summary>„Verifică o cheie existentă” (11.4): parola, cheia privata si, daca e data, amprenta instalarii.</summary>
    public static RecoveryKeyCheck Check(string pfxPath, string password, string? expectedThumbprint)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, password, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            return RecoveryKeyCheck.WrongPassword;
        }

        using (certificate)
        {
            if (!certificate.HasPrivateKey)
            {
                return RecoveryKeyCheck.NoPrivateKey;
            }

            return expectedThumbprint is not null && !string.Equals(certificate.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase)
                ? RecoveryKeyCheck.OtherInstallation
                : RecoveryKeyCheck.Valid;
        }
    }

    /// <summary>Drept de citire pe fisierul cheii private pentru identitatile pool-ului si serviciului (I-08; „Repară” il reaplica).</summary>
    public static void GrantPrivateKeyRead(string thumbprint, IEnumerable<SecurityIdentifier> identities)
    {
        using var certificate = Find(thumbprint)
            ?? throw new WindowsSetupException($"Certificatul de protecție a datelor ({thumbprint}) lipsește din LocalMachine\\My.");
        var keyFile = PrivateKeyFile(certificate)
            ?? throw new WindowsSetupException($"Fișierul cheii private a certificatului {thumbprint} nu a fost găsit.");

        var file = new FileInfo(keyFile);
        var security = file.GetAccessControl();
        foreach (var identity in identities.Distinct())
        {
            security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.Read | FileSystemRights.Synchronize, AccessControlType.Allow));
        }

        file.SetAccessControl(security);
    }

    private static string AddToMachineStore(byte[] pkcs12, string? password, string instance)
    {
        using var persisted = X509CertificateLoader.LoadPkcs12(pkcs12, password,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        persisted.FriendlyName = $"ERP-{instance} Data Protection";
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        store.Add(persisted);
        return persisted.Thumbprint;
    }

    /// <summary>Cheile CNG stau in Crypto\Keys, cele CAPI in Crypto\RSA\MachineKeys (dupa furnizorul ales la import).</summary>
    private static string? PrivateKeyFile(X509Certificate2 certificate)
    {
        using var key = certificate.GetRSAPrivateKey();
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string? candidate = key switch
        {
            RSACng cng => cng.Key.UniqueName is { } name ? Path.Combine(programData, "Microsoft", "Crypto", "Keys", name) : null,
            RSACryptoServiceProvider capi => Path.Combine(programData, "Microsoft", "Crypto", "RSA", "MachineKeys",
                capi.CspKeyContainerInfo.UniqueKeyContainerName),
            _ => null,
        };
        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }
}

/// <summary>Cheia de recuperare lipseste, nu corespunde instalarii sau parola ei e gresita (cod 80).</summary>
public sealed class RecoveryKeyException(RecoveryKeyCheck check) : Exception(check switch
{
    RecoveryKeyCheck.WrongPassword => "Parola cheii de recuperare e greșită sau fișierul nu e o cheie de recuperare.",
    RecoveryKeyCheck.NoPrivateKey => "Fișierul nu conține cheia privată.",
    RecoveryKeyCheck.OtherInstallation => "Cheia de recuperare aparține altei instalări.",
    _ => "Cheia de recuperare nu e validă.",
})
{
    public RecoveryKeyCheck Check { get; } = check;

    public int ExitCode => Core.ExitCodes.RecoveryKeyInvalid;
}
