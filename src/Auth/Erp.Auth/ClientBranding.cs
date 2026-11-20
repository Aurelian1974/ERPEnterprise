using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Erp.Auth;

/// <summary>
/// Sigla clientului de pe paginile /account/* (02.01, 11: „Poartă numele și sigla clientului”).
/// Fisierul se pune la instalare in app\host\wwwroot\brand\; calea se poate schimba cu
/// <c>Branding:LogoPath</c>. Sigla din baza de date (Company.LogoAttachmentId) asteapta atasamentele
/// (etapa 10 a Nucleului), vezi 10 Interfata si tema vizuala, secțiunea 9.
/// </summary>
public sealed class ClientBranding
{
    /// <summary>Fisierele incercate in lipsa unei setari, in ordine.</summary>
    internal static readonly string[] DefaultCandidates =
        ["brand/logo.svg", "brand/logo.png", "brand/logo.webp", "brand/logo.jpg"];

    public ClientBranding(IConfiguration configuration, IWebHostEnvironment environment)
        : this(configuration["Branding:LogoPath"], path => environment.WebRootFileProvider.GetFileInfo(path).Exists)
    {
    }

    internal ClientBranding(string? configuredPath, Func<string, bool> exists)
    {
        LogoUrl = ResolveLogo(configuredPath, exists);
    }

    /// <summary>Calea web a siglei (de exemplu <c>/brand/logo.svg</c>) sau null daca nu exista fisier.</summary>
    public string? LogoUrl { get; }

    /// <summary>
    /// Alege sigla: setarea, daca fisierul ei exista, altfel primul candidat implicit gasit. O setare
    /// care arata spre un fisier lipsa nu cade spre candidati: ar ascunde greseala de configurare.
    /// </summary>
    internal static string? ResolveLogo(string? configuredPath, Func<string, bool> exists)
    {
        var configured = configuredPath?.Trim();
        if (!string.IsNullOrEmpty(configured))
        {
            var normalized = configured.Replace('\\', '/').TrimStart('/');
            return normalized.Length > 0 && !normalized.Contains("..", StringComparison.Ordinal) && exists(normalized)
                ? "/" + normalized
                : null;
        }

        return DefaultCandidates.FirstOrDefault(exists) is { } found ? "/" + found : null;
    }

    /// <summary>Initiala afisata cand nu exista fisier de siglă; „?” daca nu exista nici nume.</summary>
    public static string Initial(string? customerName)
    {
        var name = customerName?.TrimStart() ?? string.Empty;
        return name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
    }
}
