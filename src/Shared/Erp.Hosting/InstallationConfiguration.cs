using Erp.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Erp.Hosting;

/// <summary>
/// Configurarea instalarii (8.10): appsettings.Installation.json din ERP_INSTANCE_DIR\config\ si secretele DPAPI
/// (secrets.app.bin), ca perechi de configurare. Sirul de conexiune se construieste cu SqlConnectionStringBuilder.
/// </summary>
public static class InstallationConfiguration
{
    public static IConfigurationBuilder AddErpInstallation(this IConfigurationBuilder builder, string instanceDirectory)
    {
        var paths = new InstanceDataPaths(instanceDirectory);
        builder.AddJsonFile(paths.AppSettings, optional: false, reloadOnChange: false);

        var instance = new ConfigurationBuilder().AddJsonFile(paths.AppSettings, optional: false).Build()["Erp:Instance"];
        if (OperatingSystem.IsWindows() && File.Exists(paths.Secrets) && !string.IsNullOrEmpty(instance))
        {
            builder.AddInMemoryCollection(InstallationSecrets.Read(paths.Secrets, instance).Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
        }

        return builder;
    }

    /// <summary>Application Name = ERP-&lt;Instanta&gt;-&lt;Componenta&gt; (de exemplu ERP-PROD-Host), ca sesiunile sa se vada in SQL Server.</summary>
    public static string ConnectionString(IConfiguration configuration, string component)
    {
        var database = configuration.GetSection("Database");
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Required(database, "Server"),
            InitialCatalog = Required(database, "Name"),
            Encrypt = database["Encrypt"] switch
            {
                "Strict" => SqlConnectionEncryptOption.Strict,
                "Optional" => SqlConnectionEncryptOption.Optional,
                _ => SqlConnectionEncryptOption.Mandatory,
            },
            TrustServerCertificate = string.Equals(database["TrustServerCertificate"], "true", StringComparison.OrdinalIgnoreCase),
            CommandTimeout = int.TryParse(database["CommandTimeoutSeconds"], out var timeout) ? timeout : 30,
            ApplicationName = $"ERP-{configuration["Erp:Instance"]}-{component}",
        };

        if (string.Equals(database["Authentication"], "Windows", StringComparison.Ordinal))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = Required(database, "UserId");
            builder.Password = database["Password"] ?? throw new InvalidOperationException("Parola bazei lipsește din secrets.app.bin.");
        }

        return builder.ConnectionString;
    }

    private static string Required(IConfigurationSection section, string key) =>
        section[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{section.Path}:{key} lipsește din configurare.");
}
