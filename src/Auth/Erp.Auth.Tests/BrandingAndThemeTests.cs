using System.Text.Json;

namespace Erp.Auth.Tests;

/// <summary>
/// Sigla clientului (02.01, 11) si legatura paginilor /account/* cu paleta temei
/// (ST5; Docs/arhitectura/10-interfata-tema/INTERFATA_TEMA.md, secțiunea 7).
/// </summary>
public sealed class BrandingAndThemeTests
{
    [Fact]
    public void Logo_setting_is_used_when_the_file_exists()
    {
        var branding = new ClientBranding("brand/sigla-client.svg", path => path == "brand/sigla-client.svg");

        Assert.Equal("/brand/sigla-client.svg", branding.LogoUrl);
    }

    [Theory]
    [InlineData("/brand/logo.png")]
    [InlineData("brand\\logo.png")]
    public void Logo_setting_accepts_a_leading_slash_and_windows_separators(string configured)
    {
        Assert.Equal("/brand/logo.png", new ClientBranding(configured, path => path == "brand/logo.png").LogoUrl);
    }

    [Fact]
    public void A_setting_that_points_to_a_missing_file_does_not_fall_back_silently()
    {
        // Ar ascunde greseala de configurare: administratorul ar vedea initiala si ar crede ca sigla e pusa.
        var branding = new ClientBranding("brand/lipsa.svg", path => path == "brand/logo.png");

        Assert.Null(branding.LogoUrl);
    }

    [Fact]
    public void A_setting_cannot_escape_wwwroot()
    {
        Assert.Null(new ClientBranding("../appsettings.json", _ => true).LogoUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_setting_the_default_files_are_tried_in_order(string? configured)
    {
        Assert.Equal("/brand/logo.svg", new ClientBranding(configured, _ => true).LogoUrl);
        Assert.Equal("/brand/logo.png", new ClientBranding(configured, path => path != "brand/logo.svg").LogoUrl);
        Assert.Null(new ClientBranding(configured, _ => false).LogoUrl);
    }

    [Theory]
    [InlineData("Grupul Exemplu", "G")]
    [InlineData("  exemplu srl", "E")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Without_a_logo_file_the_initial_of_the_customer_name_is_shown(string? customer, string expected)
    {
        Assert.Equal(expected, ClientBranding.Initial(customer));
    }

    /// <summary>
    /// Paleta are o singura sursa: fisierul aplicatiei React, legat in Erp.Auth si servit ca
    /// _content/Erp.Auth/tokens.css. Daca legatura din Erp.Auth.csproj se pierde, pagina de
    /// autentificare ar ramane fara culori, iar testul cade aici, nu la ochi.
    /// </summary>
    [Fact]
    public void The_palette_is_served_as_a_static_asset_of_the_library()
    {
        var manifest = Path.Combine(AppContext.BaseDirectory, "Erp.Auth.staticwebassets.endpoints.json");
        Assert.True(File.Exists(manifest), $"Manifestul activelor statice lipsește: {manifest}");

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var routes = document.RootElement.GetProperty("Endpoints").EnumerateArray()
            .Select(endpoint => endpoint.GetProperty("Route").GetString())
            .ToArray();

        Assert.Contains("tokens.css", routes);
    }
}
