using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Erp.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Setup.Tests.Sql;

/// <summary>Inelul de chei Data Protection in core.DataProtectionKey (08, 5.12) pe SQL Server real.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Criteriu", "08-IA-14")]
public sealed class DataProtectionIntegrationTests(SqlServerFixture sql)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ring_in_the_database_lets_another_process_read_the_data_with_the_same_certificate()
    {
        var connectionString = await sql.CreateErpDatabaseAsync("ERP_IT_DPKEYS", Ct);
        var repository = new SqlXmlRepository(() => new SqlConnection(connectionString));
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=ERP-IT Data Protection", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));

        IDataProtector Protector()
        {
            var services = new ServiceCollection();
            services.AddDataProtection().SetApplicationName("ERP-IT")
                .AddKeyManagementOptions(options => options.XmlRepository = repository)
                .ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
            return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>().CreateProtector("Erp.Test");
        }

        var secret = Protector().Protect("parola-smtp");

        Assert.Equal("parola-smtp", Protector().Unprotect(secret));
        var stored = repository.GetAllElements();
        Assert.NotEmpty(stored);
        Assert.All(stored, e => Assert.Contains("EncryptedData", e.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Repository_returns_the_stored_elements_in_order()
    {
        var connectionString = await sql.CreateErpDatabaseAsync("ERP_IT_DPKEYS2", Ct);
        var repository = new SqlXmlRepository(() => new SqlConnection(connectionString));

        repository.StoreElement(new XElement("key", new XAttribute("id", "1")), "key-1");
        repository.StoreElement(new XElement("key", new XAttribute("id", "2")), "key-2");

        Assert.Equal(["1", "2"], repository.GetAllElements().Select(e => e.Attribute("id")!.Value));
    }
}
