using Erp.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Erp.Hosting;

/// <summary>
/// Configurarea instalarii (08, 5.11): appsettings.Installation.json din ERP_INSTANCE_DIR\config\, fara secrete. Conexiunea SQL
/// foloseste numai Windows authentication, cu identitatea procesului (R5, D3).
/// </summary>
public static class InstallationConfiguration
{
    public static IConfigurationBuilder AddErpInstallation(this IConfigurationBuilder builder, string instanceDirectory) =>
        builder.AddJsonFile(new InstanceDataPaths(instanceDirectory).AppSettings, optional: false, reloadOnChange: false);

    /// <summary>Application Name = ERP-&lt;Instanta&gt;-&lt;Componenta&gt; (de exemplu ERP-PROD-Host), ca sesiunile sa se vada in SQL Server.</summary>
    public static string ConnectionString(IConfiguration configuration, string component)
    {
        var database = configuration.GetSection("Database");
        return new SqlConnectionStringBuilder
        {
            DataSource = Required(database, "Server"),
            InitialCatalog = Required(database, "Name"),
            IntegratedSecurity = true,
            Encrypt = database["Encrypt"] switch
            {
                "Strict" => SqlConnectionEncryptOption.Strict,
                "Optional" => SqlConnectionEncryptOption.Optional,
                _ => SqlConnectionEncryptOption.Mandatory,
            },
            TrustServerCertificate = string.Equals(database["TrustServerCertificate"], "true", StringComparison.OrdinalIgnoreCase),
            CommandTimeout = int.TryParse(database["CommandTimeoutSeconds"], out var timeout) ? timeout : 30,
            ApplicationName = $"ERP-{configuration["Erp:Instance"]}-{component}",
        }.ConnectionString;
    }

    private static string Required(IConfigurationSection section, string key) =>
        section[key] is { Length: > 0 } value ? value : throw new InvalidOperationException($"{section.Path}:{key} lipsește din configurare.");
}
