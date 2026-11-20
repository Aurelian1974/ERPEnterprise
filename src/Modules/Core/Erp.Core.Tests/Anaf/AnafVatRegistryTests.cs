using System.Diagnostics;
using System.Net;
using System.Text;
using Erp.Core.Application.Anaf;
using Erp.Core.Application.Settings;
using Erp.Core.Contracts;
using Erp.Core.Infrastructure.Anaf;
using Microsoft.Extensions.Logging.Abstractions;

namespace Erp.Core.Tests.Anaf;

/// <summary>Interpretarea raspunsului ANAF v9 pe raspunsuri reale salvate (fisiere de referinta) si regulile clientului.</summary>
public sealed class AnafVatRegistryTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Anaf", name));

    [Fact]
    public void Vat_payer_response_maps_to_partner_fields()
    {
        var company = AnafVatResponse.Parse(Fixture("anaf-v9-platitor.json"), "14080700", Now)!;

        Assert.Equal("REALITATEA MEDIA SA", company.Name);
        Assert.Equal("J40/6736/2001", company.TradeRegisterNo);
        Assert.True(company.VatPayer);
        Assert.False(company.VatOnCollection);
        Assert.False(company.AnafInactive);
        Assert.Equal("Str. Frumoasă nr. 20", company.Address);
        // Diacriticele ANAF (sedila) devin cele corecte (virgula); codul postal isi recapata zeroul din fata.
        Assert.Equal("Sector 1 Mun. București", company.City);
        Assert.Equal("B", company.CountyCode);
        Assert.Equal("010987", company.PostalCode);
        Assert.Equal(Now, company.CheckedAt);
    }

    [Fact]
    public void Deregistered_company_is_not_a_vat_payer_and_keeps_its_registration_state()
    {
        var company = AnafVatResponse.Parse(Fixture("anaf-v9-radiat.json"), "12345674", Now)!;

        Assert.False(company.VatPayer);
        Assert.StartsWith("RADIERE", company.RegistrationState, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_cui_and_malformed_response_are_distinguished()
    {
        Assert.Null(AnafVatResponse.Parse("""{"found":[],"notFound":[14080700]}""", "14080700", Now));
        Assert.Null(AnafVatResponse.Parse(Fixture("anaf-v9-platitor.json"), "12345674", Now));
        Assert.Throws<System.Text.Json.JsonException>(() => AnafVatResponse.Parse("""{"cod":200}""", "14080700", Now));
    }

    [Fact]
    public async Task Client_posts_to_the_configured_address_and_keeps_one_request_per_second()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fixture("anaf-v9-platitor.json"), Encoding.UTF8, "application/json") });
        using var http = new AnafHttp(handler, TimeProvider.System);
        var registry = new AnafVatRegistry(http, new FakeSettings("https://anaf.test/api/PlatitorTvaRest/v10/tva"), TimeProvider.System, NullLogger<AnafVatRegistry>.Instance);

        var watch = Stopwatch.StartNew();
        await registry.LookupAsync("14080700", new DateOnly(2026, 11, 20), TestContext.Current.CancellationToken);
        await registry.LookupAsync("14080700", new DateOnly(2026, 11, 20), TestContext.Current.CancellationToken);
        watch.Stop();

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://anaf.test/api/PlatitorTvaRest/v10/tva", handler.Requests[0].Url);
        Assert.Equal("""[{"cui":14080700,"data":"2026-11-20"}]""", handler.Requests[0].Body);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(950), $"A doua cerere a plecat după {watch.Elapsed}.");
    }

    [Fact]
    public async Task Empty_404_is_not_found_while_errors_and_timeouts_are_unavailable()
    {
        using var empty = new AnafHttp(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("""{"found":[],"notFound":[]}""") }), TimeProvider.System);
        Assert.Null(await Registry(empty).LookupAsync("14080700", new DateOnly(2026, 11, 20), TestContext.Current.CancellationToken));

        using var failing = new AnafHttp(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("eroare") }), TimeProvider.System);
        var error = await Assert.ThrowsAsync<ErpException>(() => Registry(failing).LookupAsync("14080700", new DateOnly(2026, 11, 20), TestContext.Current.CancellationToken));
        Assert.Equal((ErpErrorKind.Unavailable, AnafErrorCodes.Unavailable), (error.Kind, error.Code));

        using var down = new AnafHttp(new FakeHandler(_ => throw new HttpRequestException("fără rețea")), TimeProvider.System);
        var offline = await Assert.ThrowsAsync<ErpException>(() => Registry(down).LookupAsync("14080700", new DateOnly(2026, 11, 20), TestContext.Current.CancellationToken));
        Assert.Equal(ErpErrorKind.Unavailable, offline.Kind);

        using var html = new AnafHttp(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>mentenanță</html>") }), TimeProvider.System);
        var garbage = await Assert.ThrowsAsync<ErpException>(() => Registry(html).LookupAsync("14080700", new DateOnly(2026, 11, 20), TestContext.Current.CancellationToken));
        Assert.Equal(AnafErrorCodes.Unavailable, garbage.Code);
    }

    private static AnafVatRegistry Registry(AnafHttp http) =>
        new(http, new FakeSettings(null), TimeProvider.System, NullLogger<AnafVatRegistry>.Instance);

    private sealed class FakeSettings(string? url) : ISettingsStore
    {
        public Task<SettingValue> GetInstallationAsync(string key, CancellationToken ct) => Task.FromResult(new SettingValue(url, null));

        public Task<byte[]> SaveInstallationAsync(string key, string? value, byte[]? rowVersion, int userId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
