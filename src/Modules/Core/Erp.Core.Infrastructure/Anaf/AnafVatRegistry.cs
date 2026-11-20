using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Erp.Core.Application.Anaf;
using Erp.Core.Application.Settings;
using Erp.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Erp.Core.Infrastructure.Anaf;

/// <summary>
/// Conexiunea comuna spre ANAF (singleton): un singur HttpClient si limita serviciului, cel mult o cerere pe secunda pentru
/// toata instalarea (instructiunile ANAF: „maxim 1 request pe secunda”; suprasolicitarea se sanctioneaza).
/// </summary>
public sealed class AnafHttp(HttpMessageHandler handler, TimeProvider clock) : IDisposable
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);

    // ANAF recomanda un timeout de cel putin 10 secunde.
    private readonly HttpClient client = new(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset? last;

    public AnafHttp(TimeProvider clock)
        : this(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), UseProxy = true }, clock)
    {
    }

    public async Task<HttpResponseMessage> PostAsync(Uri url, object body, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (last is { } previous && clock.GetUtcNow() - previous < MinInterval)
            {
                await Task.Delay(MinInterval - (clock.GetUtcNow() - previous), clock, ct).ConfigureAwait(false);
            }

            try
            {
                return await client.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);
            }
            finally
            {
                last = clock.GetUtcNow();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        client.Dispose();
        gate.Dispose();
    }
}

/// <summary>
/// Serviciul web ANAF de verificare a contribuabililor (sincron; implicit v9, adresa din setarea Anaf.VatRegistryUrl).
/// Cererea: POST cu [{"cui": numar, "data": "AAAA-LL-ZZ"}]; raspunsul: {"found": [...], "notFound": [...]}.
/// </summary>
public sealed class AnafVatRegistry(AnafHttp http, ISettingsStore settings, TimeProvider clock, ILogger<AnafVatRegistry> log) : IAnafVatRegistry
{
    /// <summary>Limita ANAF de CUI-uri intr-o cerere.</summary>
    public const int MaxCodesPerRequest = 100;

    public async Task<AnafCompanyDto?> LookupAsync(string fiscalCode, DateOnly date, CancellationToken ct) =>
        (await LookupManyAsync([fiscalCode], date, ct).ConfigureAwait(false)).GetValueOrDefault(fiscalCode);

    public async Task<IReadOnlyDictionary<string, AnafCompanyDto>> LookupManyAsync(IReadOnlyCollection<string> fiscalCodes, DateOnly date, CancellationToken ct)
    {
        var result = new Dictionary<string, AnafCompanyDto>(StringComparer.Ordinal);
        if (fiscalCodes.Count == 0)
        {
            return result;
        }

        var configured = (await settings.GetInstallationAsync(SettingKeys.AnafVatRegistryUrl, ct).ConfigureAwait(false)).Value;
        var url = new Uri(string.IsNullOrWhiteSpace(configured) ? SettingKeys.DefaultAnafVatRegistryUrl : configured);
        var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var chunk in fiscalCodes.Distinct(StringComparer.Ordinal).Chunk(MaxCodesPerRequest))
        {
            var body = chunk.Select(code => new { cui = long.Parse(code, CultureInfo.InvariantCulture), data = day }).ToArray();
            var json = await PostAsync(url, body, ct).ConfigureAwait(false);
            try
            {
                foreach (var company in AnafVatResponse.ParseAll(json, clock.GetUtcNow().UtcDateTime))
                {
                    result[company.FiscalCode] = company;
                }
            }
            catch (JsonException ex)
            {
                log.LogWarning(ex, "Răspuns ANAF neașteptat de la {Url}", url);
                throw Unavailable("Răspunsul ANAF nu are forma așteptată. Verificați adresa serviciului din Configurare › Setări (versiunea curentă publicată de ANAF).");
            }
        }

        return result;
    }

    private async Task<string> PostAsync(Uri url, object body, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsync(url, body, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // Serviciul v9 raspunde 404 cu {"found":[],"notFound":[...]} cand niciun CUI nu e gasit; alte coduri sunt erori.
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                log.LogWarning("ANAF a răspuns {Status} la {Url}", (int)response.StatusCode, url);
                throw Unavailable($"Serviciul ANAF a răspuns cu eroarea {(int)response.StatusCode}. Reîncercați mai târziu sau verificați adresa din Configurare › Setări.");
            }

            return json;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "ANAF nu răspunde la {Url}", url);
            throw Unavailable("Serviciul ANAF nu răspunde. Reîncercați mai târziu; dacă problema persistă, verificați accesul serverului la internet.");
        }
    }

    private static ErpException Unavailable(string message) => new(ErpErrorKind.Unavailable, AnafErrorCodes.Unavailable, message);
}

/// <summary>
/// Raspunsul serviciului ANAF transformat in datele fisei de partener. Forma reala (v9, verificata pe 20.11.2026) difera putin de
/// instructiuni: fara „cod”/„message”, perioade_TVA e lista, codul postal pierde zeroul din fata, diacriticele sunt cu sedila.
/// </summary>
public static class AnafVatResponse
{
    /// <exception cref="JsonException">Raspunsul nu are forma serviciului.</exception>
    public static AnafCompanyDto? Parse(string json, string fiscalCode, DateTime checkedAtUtc) =>
        ParseAll(json, checkedAtUtc).FirstOrDefault(c => c.FiscalCode == fiscalCode);

    /// <summary>Toti contribuabilii gasiti din raspuns.</summary>
    /// <exception cref="JsonException">Raspunsul nu are forma serviciului.</exception>
    public static IReadOnlyList<AnafCompanyDto> ParseAll(string json, DateTime checkedAtUtc)
    {
        var result = new List<AnafCompanyDto>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("found", out var found) || found.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Lipsește lista „found”.");
        }

        foreach (var item in found.EnumerateArray())
        {
            var general = Object(item, "date_generale");
            var fiscalCode = Text(general, "cui");
            if (fiscalCode is null)
            {
                continue;
            }

            var office = Object(item, "adresa_sediu_social");
            var street = Text(office, "sdenumire_Strada");
            var number = Text(office, "snumar_Strada");
            var details = Text(office, "sdetalii_Adresa");
            var address = street is null
                ? Text(general, "adresa")
                : string.Join(", ", new[] { number is null ? street : $"{street} nr. {number}", details }.Where(p => p is not null));
            result.Add(new AnafCompanyDto(
                fiscalCode,
                Text(general, "denumire") ?? throw new JsonException("Lipsește denumirea."),
                Text(general, "nrRegCom"),
                Bool(Object(item, "inregistrare_scop_Tva"), "scpTVA"),
                Bool(Object(item, "inregistrare_RTVAI"), "statusTvaIncasare"),
                Bool(Object(item, "stare_inactiv"), "statusInactivi"),
                Bool(Object(item, "inregistrare_SplitTVA"), "statusSplitTVA"),
                Bool(general, "statusRO_e_Factura"),
                Text(general, "stare_inregistrare"),
                address,
                Text(office, "sdenumire_Localitate"),
                Text(office, "scod_JudetAuto"),
                PostalCode(Text(office, "scod_Postal") ?? Text(general, "codPostal")),
                Text(general, "telefon"),
                DateTime.SpecifyKind(checkedAtUtc, DateTimeKind.Utc)));
        }

        return result;
    }

    /// <summary>Codul postal romanesc are 6 cifre; ANAF il trimite ca numar (010987 → „10987”).</summary>
    public static string? PostalCode(string? value) =>
        value is { Length: > 0 and < 6 } && value.All(char.IsAsciiDigit) ? value.PadLeft(6, '0') : value;

    /// <summary>ANAF scrie ş/ţ cu sedila; in romana corecta (si in nomenclatoare) sunt ș/ț cu virgula.</summary>
    public static string FixDiacritics(string value) =>
        value.Replace('ş', 'ș').Replace('Ş', 'Ș').Replace('ţ', 'ț').Replace('Ţ', 'Ț');

    private static JsonElement Object(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static string? Text(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : FixDiacritics(text.Trim());
    }

    private static bool Bool(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
        && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase)));
}
