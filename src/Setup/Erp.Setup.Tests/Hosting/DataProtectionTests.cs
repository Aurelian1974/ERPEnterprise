using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Erp.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Setup.Tests.Hosting;

/// <summary>Inelul de chei, ca in baza: comun pentru toate procesele care il citesc (08, 5.12).</summary>
internal sealed class MemoryXmlRepository : IXmlRepository
{
    private readonly List<XElement> elements = [];

    public IReadOnlyCollection<XElement> GetAllElements() => elements.Select(e => new XElement(e)).ToList();

    public void StoreElement(XElement element, string friendlyName) => elements.Add(new XElement(element));

    public string Content => string.Concat(elements.Select(e => e.ToString()));
}

/// <summary>
/// Portabilitatea datelor criptate (08, 5.12, D22): cu acelasi inel de chei (baza) si acelasi certificat (cheia de recuperare),
/// alt proces sau alt server citeste datele; fara certificat, nu.
/// </summary>
[Trait("Criteriu", "08-IA-14")]
public sealed class DataProtectionTests : IDisposable
{
    private readonly X509Certificate2 installation = Certificate("ERP-PROD Data Protection");
    private readonly X509Certificate2 other = Certificate("Alt certificat");
    private readonly MemoryXmlRepository ring = new();

    public void Dispose()
    {
        installation.Dispose();
        other.Dispose();
    }

    private static X509Certificate2 Certificate(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, critical: true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
    }

    private IDataProtector Protector(X509Certificate2 certificate)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("ERP-PROD")
            .AddKeyManagementOptions(options => options.XmlRepository = ring)
            .ProtectKeysWithCertificate(certificate)
            .UnprotectKeysWithAnyCertificate(certificate);
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>().CreateProtector("Erp.Auth.AuthenticatorKey.v1");
    }

    [Fact]
    public void Data_protected_by_one_server_is_read_by_another_with_the_same_ring_and_certificate()
    {
        var secret = Protector(installation).Protect("JBSWY3DPEHPK3PXP");

        Assert.Equal("JBSWY3DPEHPK3PXP", Protector(installation).Unprotect(secret));
    }

    [Fact]
    public void Keys_in_the_ring_are_encrypted_with_the_installation_certificate()
    {
        Protector(installation).Protect("x");

        Assert.Contains("EncryptedData", ring.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", ring.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_installation_certificate_the_data_cannot_be_read()
    {
        var secret = Protector(installation).Protect("JBSWY3DPEHPK3PXP");

        Assert.ThrowsAny<CryptographicException>(() => Protector(other).Unprotect(secret));
    }

    [Fact]
    public async Task Health_check_is_healthy_when_the_ring_works_and_unhealthy_when_it_cannot_be_read()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        var healthy = new DataProtectionCheck(services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>());

        Assert.Equal("dataProtection", healthy.Name);
        Assert.Equal(HealthStatus.Healthy, await healthy.CheckAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HealthStatus.Unhealthy, await new DataProtectionCheck(new BrokenProvider()).CheckAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Production_without_the_certificate_thumbprint_refuses_to_start()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp:Instance"] = "PROD" }).Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddErpDataProtection(configuration, () => throw new InvalidOperationException(), development: false));
        Assert.Contains(ErpDataProtection.CertificateThumbprintKey, ex.Message, StringComparison.Ordinal);
        new ServiceCollection().AddErpDataProtection(configuration, () => throw new InvalidOperationException(), development: true);
    }

    private sealed class BrokenProvider : IDataProtectionProvider, IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;

        public byte[] Protect(byte[] plaintext) => throw new CryptographicException("Inelul de chei nu se poate citi.");

        public byte[] Unprotect(byte[] protectedData) => throw new CryptographicException("Inelul de chei nu se poate citi.");
    }
}
