using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Erp.Bnr;

/// <summary>Un curs de referinta BNR: lei pentru <see cref="Multiplier"/> unitati din moneda (HUF, JPY, KRW se publica la 100).</summary>
public sealed record BnrRate(string Currency, DateOnly Date, decimal Rate, int Multiplier);

/// <summary>Fisierul primit nu are formatul cursurilor BNR (de exemplu o pagina de eroare HTML in locul XML-ului).</summary>
public sealed class BnrFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Citirea fisierelor XML ale BNR (zilnic, ultimele 10 zile, arhiva anuala): elementele <c>Cube</c> cu atributul <c>date</c> si, in
/// ele, <c>Rate</c> cu <c>currency</c>, <c>multiplier</c> optional si valoarea cu punct zecimal. Elementele se cauta dupa numele
/// local, deci schimbarea spatiului de nume (http://www.bnr.ro/xsd in arhive, https://… din 2026) nu afecteaza citirea.
/// </summary>
public static class BnrRatesParser
{
    public static IReadOnlyList<BnrRate> Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException ex)
        {
            throw new BnrFormatException("Răspunsul BNR nu este un fișier XML de cursuri.", ex);
        }

        var cubes = document.Descendants().Where(e => e.Name.LocalName == "Cube" && e.Attribute("date") is not null).ToList();
        if (cubes.Count == 0)
        {
            throw new BnrFormatException("Fișierul BNR nu conține nicio zi de curs (elementul Cube cu data).");
        }

        var rates = new List<BnrRate>();
        foreach (var cube in cubes)
        {
            if (!DateOnly.TryParseExact(cube.Attribute("date")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new BnrFormatException($"Data „{cube.Attribute("date")!.Value}” din fișierul BNR nu are formatul aaaa-ll-zz.");
            }

            foreach (var rate in cube.Elements().Where(e => e.Name.LocalName == "Rate"))
            {
                var currency = rate.Attribute("currency")?.Value.Trim().ToUpperInvariant();
                if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
                {
                    throw new BnrFormatException($"Un curs din {date:yyyy-MM-dd} nu are codul monedei.");
                }

                if (!decimal.TryParse(rate.Value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || value <= 0)
                {
                    throw new BnrFormatException($"Cursul {currency} din {date:yyyy-MM-dd} nu este un număr pozitiv.");
                }

                var multiplier = 1;
                if (rate.Attribute("multiplier") is { } m
                    && (!int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out multiplier) || multiplier <= 0))
                {
                    throw new BnrFormatException($"Multiplicatorul cursului {currency} din {date:yyyy-MM-dd} nu este valid.");
                }

                rates.Add(new BnrRate(currency, date, value, multiplier));
            }
        }

        return rates;
    }
}

/// <summary>Adresele fisierelor BNR (configurabile: Bnr:DailyUrl, Bnr:TenDaysUrl).</summary>
public sealed record BnrOptions(Uri DailyUrl, Uri TenDaysUrl)
{
    public static readonly BnrOptions Default = new(new Uri("https://www.bnr.ro/nbrfxrates.xml"), new Uri("https://www.bnr.ro/nbrfxrates10days.xml"));
}

/// <summary>
/// Clientul HTTP pentru fisierele BNR. BNR cere HTTPS (TLS 1.2+) si recomanda sa nu se descarce mai des decat e nevoie: fisierul
/// se actualizeaza o data pe zi bancara, putin dupa ora 13:00.
/// </summary>
public sealed class BnrRatesClient(HttpClient http)
{
    public async Task<IReadOnlyList<BnrRate>> GetAsync(Uri url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"BNR a răspuns {(int)response.StatusCode} la {url}.", null, response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return BnrRatesParser.Parse(body);
    }
}
