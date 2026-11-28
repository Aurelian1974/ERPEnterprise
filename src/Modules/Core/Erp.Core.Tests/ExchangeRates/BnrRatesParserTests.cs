using Erp.Bnr;

namespace Erp.Core.Tests.ExchangeRates;

/// <summary>
/// Citirea fisierelor BNR (02.01, 4.3). Exemplele urmeaza structura publicata de BNR (DataSet / Header / Body / Cube cu data / Rate cu
/// currency si multiplier); valorile sunt de test. Spatiul de nume se schimba din 2026 (https), deci parserul il ignora.
/// </summary>
public sealed class BnrRatesParserTests
{
    private static string Sample(string ns, params (string Date, string Rates)[] days) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <DataSet xmlns="{ns}" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <Header>
            <Publisher>National Bank of Romania</Publisher>
            <PublishingDate>{days[0].Date}</PublishingDate>
            <MessageType>DR</MessageType>
          </Header>
          <Body>
            <Subject>Reference rates</Subject>
            <OrigCurrency>RON</OrigCurrency>
            {string.Concat(days.Select(d => $"<Cube date=\"{d.Date}\">{d.Rates}</Cube>"))}
          </Body>
        </DataSet>
        """;

    private const string Rates = """<Rate currency="EUR">5.0812</Rate><Rate currency="USD">4.3794</Rate><Rate currency="HUF" multiplier="100">1.3125</Rate>""";

    [Theory]
    [InlineData("http://www.bnr.ro/xsd")]
    [InlineData("https://www.bnr.ro/xsd")]
    public void Reads_rates_with_multiplier_whatever_the_namespace(string ns)
    {
        var rates = BnrRatesParser.Parse(Sample(ns, ("2026-11-13", Rates)));

        Assert.Equal(3, rates.Count);
        Assert.Equal(new BnrRate("EUR", new DateOnly(2026, 11, 13), 5.0812m, 1), rates[0]);
        Assert.Equal(new BnrRate("HUF", new DateOnly(2026, 11, 13), 1.3125m, 100), rates[2]);
    }

    [Fact]
    public void Ten_day_file_has_one_cube_per_banking_day()
    {
        var rates = BnrRatesParser.Parse(Sample("http://www.bnr.ro/xsd", ("2026-11-12", Rates), ("2026-11-13", Rates)));

        Assert.Equal([new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 13)], rates.Select(r => r.Date).Distinct().ToArray());
    }

    [Fact]
    public void Error_page_instead_of_xml_is_refused_with_a_clear_message()
    {
        const string page = """<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "x"><html><head><title>Error</title></head><body><H2>Error</H2>This page can't be displayed.</body></html>""";

        var error = Assert.Throws<BnrFormatException>(() => BnrRatesParser.Parse(page));
        Assert.Contains("BNR", error.Message, StringComparison.Ordinal);
        Assert.Contains("pagină HTML", error.Message, StringComparison.Ordinal);
        Assert.Throws<BnrFormatException>(() => BnrRatesParser.Parse("<DataSet><Body/></DataSet>"));
        Assert.Throws<BnrFormatException>(() => BnrRatesParser.Parse(Sample("x", ("2026-11-13", """<Rate currency="EUR">5,08</Rate>"""))));
    }

    private const string FirewallPage = """<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "x"><html><body>This page can't be displayed.</body></html>""";

    [Fact]
    public void Default_addresses_use_the_dedicated_xml_host()
    {
        Assert.Equal("https://curs.bnr.ro/nbrfxrates.xml", BnrOptions.Default.DailyUrl.AbsoluteUri);
        Assert.Equal("https://curs.bnr.ro/nbrfxrates10days.xml", BnrOptions.Default.TenDaysUrl.AbsoluteUri);
    }

    [Fact]
    public async Task Client_retries_when_the_firewall_sends_an_error_page_instead_of_xml()
    {
        var handler = new QueueHandler(FirewallPage, Sample("https://www.bnr.ro/xsd", ("2026-11-27", Rates)));
        var client = new BnrRatesClient(new HttpClient(handler), TimeSpan.Zero);

        var rates = await client.GetAsync(BnrOptions.Default.DailyUrl, CancellationToken.None);

        Assert.Equal(3, rates.Count);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Client_gives_up_after_the_last_attempt_and_does_not_retry_valid_xml_without_rates()
    {
        var blocked = new QueueHandler(Enumerable.Repeat(FirewallPage, BnrRatesClient.Attempts + 1).ToArray());
        await Assert.ThrowsAsync<BnrFormatException>(() => new BnrRatesClient(new HttpClient(blocked), TimeSpan.Zero).GetAsync(BnrOptions.Default.DailyUrl, CancellationToken.None));
        Assert.Equal(BnrRatesClient.Attempts, blocked.Calls);

        var empty = new QueueHandler("<DataSet><Body/></DataSet>", Sample("x", ("2026-11-27", Rates)));
        await Assert.ThrowsAsync<BnrFormatException>(() => new BnrRatesClient(new HttpClient(empty), TimeSpan.Zero).GetAsync(BnrOptions.Default.DailyUrl, CancellationToken.None));
        Assert.Equal(1, empty.Calls);
    }

    private sealed class QueueHandler(params string[] bodies) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(bodies[Calls++]) });
    }
}
