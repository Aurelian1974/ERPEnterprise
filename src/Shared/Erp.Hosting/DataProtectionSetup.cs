using System.Collections.ObjectModel;
using System.Data;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Hosting;

/// <summary>Inelul de chei Data Protection in core.DataProtectionKey (08, 5.12), prin procedurile core.usp_DataProtectionKey_*.</summary>
public sealed class SqlXmlRepository(Func<SqlConnection> connect) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var connection = connect();
        connection.Open();
        using var command = new SqlCommand("core.usp_DataProtectionKey_List", connection) { CommandType = CommandType.StoredProcedure };
        using var reader = command.ExecuteReader();
        var elements = new List<XElement>();
        while (reader.Read())
        {
            elements.Add(XElement.Parse(reader.GetString(1)));
        }

        return new ReadOnlyCollection<XElement>(elements);
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var connection = connect();
        connection.Open();
        using var command = new SqlCommand("core.usp_DataProtectionKey_Insert", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.Add(new SqlParameter("@FriendlyName", SqlDbType.NVarChar, 200) { Value = (object?)friendlyName ?? DBNull.Value });
        command.Parameters.Add(new SqlParameter("@Xml", SqlDbType.NVarChar, -1) { Value = element.ToString(SaveOptions.DisableFormatting) });
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// Data Protection pentru Erp.Host si Erp.Worker (08, 5.12, decizia D22): inelul de chei in baza, protejat cu certificatul
/// instalarii (LocalMachine\My, amprenta din DataProtection:CertificateThumbprint). Asa, o baza restaurata pe alt server ramane
/// lizibila daca certificatul e importat din cheia de recuperare. In Development, fara amprenta, cheile raman in profilul
/// utilizatorului.
/// </summary>
public static class ErpDataProtection
{
    public const string CertificateThumbprintKey = "DataProtection:CertificateThumbprint";

    public static IDataProtectionBuilder AddErpDataProtection(this IServiceCollection services, IConfiguration configuration,
        Func<SqlConnection> connect, bool development)
    {
        var instance = configuration["Erp:Instance"];
        var builder = services.AddDataProtection().SetApplicationName($"ERP-{instance}");
        var thumbprint = configuration[CertificateThumbprintKey];
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return development
                ? builder
                : throw new InvalidOperationException($"{CertificateThumbprintKey} lipsește din configurare (08, 5.12); rulați „Repară”.");
        }

        builder.AddKeyManagementOptions(options => options.XmlRepository = new SqlXmlRepository(connect));
        if (FindMachineCertificate(thumbprint) is { } certificate)
        {
            builder.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
        }
        else
        {
            // Certificatul lipseste (server nou fara cheia de recuperare): aplicatia porneste, dar verificarea dataProtection
            // raporteaza Unhealthy si nicio cheie noua nu se poate scrie; se repara cu „Recupereaza” (08, 11.2).
            builder.ProtectKeysWithCertificate(thumbprint);
        }

        return builder;
    }

    public static X509Certificate2? FindMachineCertificate(string thumbprint)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        return found.Count > 0 ? found[0] : null;
    }
}

/// <summary>dataProtection: certificatul instalarii e accesibil, iar cheia curenta a inelului se poate citi (08, 5.14).</summary>
public sealed class DataProtectionCheck(IDataProtectionProvider provider) : IReadinessCheck
{
    private static readonly byte[] Probe = "erp-health"u8.ToArray();

    public string Name => "dataProtection";

    public Task<HealthStatus> CheckAsync(CancellationToken ct)
    {
        try
        {
            var protector = provider.CreateProtector("Erp.Health.DataProtection.v1");
            return Task.FromResult(protector.Unprotect(protector.Protect(Probe)).AsSpan().SequenceEqual(Probe) ? HealthStatus.Healthy : HealthStatus.Unhealthy);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or InvalidOperationException or SqlException)
        {
            return Task.FromResult(HealthStatus.Unhealthy);
        }
    }
}
