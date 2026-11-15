using System.Reflection;

namespace Erp.Auth;

/// <summary>Resursele de text ale paginilor /account/* si ale mesajelor Identity (AccountText.resx, romana).</summary>
public sealed class AccountText
{
}

/// <summary>Versiunea produsului pentru subsolul paginilor (AssemblyMetadata ErpVersion a aplicatiei, 10.5).</summary>
public static class ProductInfo
{
    public static string Version { get; } = Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "ErpVersion")?.Value ?? "0.0.0";
}
