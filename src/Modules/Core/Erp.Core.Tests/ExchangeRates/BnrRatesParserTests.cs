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
        Assert.Throws<BnrFormatException>(() => BnrRatesParser.Parse("<DataSet><Body/></DataSet>"));
        Assert.Throws<BnrFormatException>(() => BnrRatesParser.Parse(Sample("x", ("2026-11-13", """<Rate currency="EUR">5,08</Rate>"""))));
    }
}
