using Administration.Application.Abstractions;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Administration.Infrastructure.Services;

public sealed class AnafService(HttpClient http, ILogger<AnafService> logger) : IAnafService
{
    private const string SyncUrl      = "https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva";
    private const string AsyncPostUrl = "https://webservicesp.anaf.ro/AsynchWebService/api/v8/ws/tva";
    private const string AsyncGetUrl  = "https://webservicesp.anaf.ro/AsynchWebService/api/v8/ws/tva";

    public async Task<AnafVerificationResult> VerifyAsync(string cui, CancellationToken ct = default)
    {
        var cuiNumeric = ParseCui(cui);
        if (cuiNumeric is null)
            return AnafVerificationResult.Failure($"CUI invalid: '{cui}'");

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var payload = new[] { new AnafRequest(cuiNumeric.Value, today) };

        // 1. Try synchronous v9 (5s timeout)
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var syncResp = await http.PostAsJsonAsync(SyncUrl, payload, cts.Token);
            if (syncResp.IsSuccessStatusCode)
            {
                var data = await syncResp.Content
                    .ReadFromJsonAsync<AnafResponse>(cancellationToken: ct);

                if (data?.Found is { Count: > 0 })
                    return AnafVerificationResult.Success(Map(data.Found[0]));

                // CUI not found in sync — return failure immediately
                if (data?.NotFound is { Count: > 0 })
                    return AnafVerificationResult.Failure($"CUI-ul {cui} nu a fost găsit în ANAF.");
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning("ANAF sincron v9 indisponibil, fallback la async v8: {Message}", ex.Message);
        }

        // 2. Fallback to asynchronous v8 POST + v7 GET
        return await VerifyAsyncV8Async(payload, ct);
    }

    private async Task<AnafVerificationResult> VerifyAsyncV8Async(
        object payload, CancellationToken ct)
    {
        AnafAsyncAcceptResponse? accepted;
        try
        {
            var acceptResp = await http.PostAsJsonAsync(AsyncPostUrl, payload, ct);
            acceptResp.EnsureSuccessStatusCode();
            accepted = await acceptResp.Content
                .ReadFromJsonAsync<AnafAsyncAcceptResponse>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ANAF async v8 — cerere inițială eșuată");
            return AnafVerificationResult.Failure("Serviciul ANAF este momentan indisponibil.");
        }

        if (accepted?.CorrelationId is null)
            return AnafVerificationResult.Failure("ANAF async v8: nu s-a primit correlationId.");

        // Polling: minim 2s per documentație, apoi 3s, 3s, 3s, 3s
        var delays = new[] { 2000, 3000, 3000, 3000, 3000 };
        foreach (var delay in delays)
        {
            await Task.Delay(delay, ct);

            try
            {
                var pollResp = await http.GetAsync(
                    $"{AsyncGetUrl}?id={accepted.CorrelationId}", ct);

                if (!pollResp.IsSuccessStatusCode) continue;

                var result = await pollResp.Content
                    .ReadFromJsonAsync<AnafResponse>(cancellationToken: ct);

                if (result?.Cod == 200 && result.Found is { Count: > 0 })
                    return AnafVerificationResult.Success(Map(result.Found[0]));

                if (result?.NotFound is { Count: > 0 })
                    return AnafVerificationResult.Failure("CUI-ul nu a fost găsit în ANAF.");

                // cod != 200 means still processing — continue polling
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ANAF async v7 — polling eșuat pentru {CorrelationId}", accepted.CorrelationId);
            }
        }

        return AnafVerificationResult.Failure("Timeout ANAF — nu s-a primit răspuns în intervalul alocat.");
    }

    private static int? ParseCui(string cui)
    {
        var normalized = cui.Trim().ToUpperInvariant().TrimStart('R', 'O').Trim();
        return int.TryParse(normalized, out var value) ? value : null;
    }

    // Response structure: found[] contains nested objects
    private static AnafCompanyData Map(AnafRawFoundItem item)
    {
        var raw = item.AdresaSediuSocial;
        var addr = raw is null ? null : new AnafAdresaSediuSocial(
            raw.Strada,
            raw.Numar,
            raw.Localitate,
            raw.Judet,
            raw.CodPostal,
            raw.Tara
        );

        return new AnafCompanyData(
            item.DateGenerale?.Denumire ?? string.Empty,
            item.DateGenerale?.Cui ?? 0,
            item.DateGenerale?.NrRegCom,
            item.DateGenerale?.Adresa,
            item.InregistrareScopTva?.ScpTva ?? false,
            item.DateGenerale?.DataInregistrare,
            item.DateGenerale?.StareInregistrare,
            item.DateGenerale?.Telefon,
            MapLegalForm(item.DateGenerale?.FormaJuridica),
            addr
        );
    }

    // Maps ANAF full legal form names to system short codes.
    // ANAF returns values like "SOCIETATE COMERCIALA CU RASPUNDERE LIMITATA"
    // (with or without diacritics). We normalize to uppercase for comparison.
    private static string? MapLegalForm(string? rawForm)
    {
        if (rawForm is null) return null;
        var upper = rawForm.ToUpperInvariant();

        if (upper.Contains("RASPUNDERE LIMITATA") || upper.Contains("RĂSPUNDERE LIMITATĂ"))
            return "SRL";
        if (upper.Contains("PE ACTIUNI") || upper.Contains("PE ACŢIUNI") || upper.Contains("PE ACȚIUNI"))
            return "SA";
        if (upper.Contains("PERSOANA FIZICA AUTORIZATA") || upper.Contains("PERSOANĂ FIZICĂ AUTORIZATĂ"))
            return "PFA";
        if (upper.Contains("REGIE AUTONOMA") || upper.Contains("REGIE AUTONOMĂ"))
            return "RA";

        return rawForm;
    }

    // ─── Raw JSON models (nested per ANAF v9 docs) ────────────────────────────

    private sealed record AnafRequest(
        [property: JsonPropertyName("cui")]  int    Cui,
        [property: JsonPropertyName("data")] string Data
    );

    // Shared response shape for both sync and async polling
    private sealed record AnafResponse(
        [property: JsonPropertyName("cod")]      int                    Cod,
        [property: JsonPropertyName("message")]  string?                Message,
        [property: JsonPropertyName("found")]    List<AnafRawFoundItem>? Found,
        [property: JsonPropertyName("notFound")] List<int>?              NotFound
    );

    private sealed record AnafAsyncAcceptResponse(
        [property: JsonPropertyName("correlationId")] string? CorrelationId,
        [property: JsonPropertyName("cod")]           int?    Cod,
        [property: JsonPropertyName("message")]       string? Message
    );

    // Top-level item in "found" array — wraps nested sections
    private sealed record AnafRawFoundItem(
        [property: JsonPropertyName("date_generale")]         AnafDateGenerale?          DateGenerale,
        [property: JsonPropertyName("inregistrare_scop_Tva")] AnafInregistrareScopTva?   InregistrareScopTva,
        [property: JsonPropertyName("adresa_sediu_social")]   AnafAdresaSediuSocialRaw?  AdresaSediuSocial
    );

    private sealed record AnafDateGenerale(
        [property: JsonPropertyName("cui")]                int?    Cui,
        [property: JsonPropertyName("denumire")]           string? Denumire,
        [property: JsonPropertyName("nrRegCom")]           string? NrRegCom,
        [property: JsonPropertyName("adresa")]             string? Adresa,
        [property: JsonPropertyName("stare_inregistrare")] string? StareInregistrare,
        [property: JsonPropertyName("data_inregistrare")]  string? DataInregistrare,
        [property: JsonPropertyName("telefon")]            string? Telefon,
        [property: JsonPropertyName("forma_juridica")]     string? FormaJuridica
    );

    private sealed record AnafInregistrareScopTva(
        [property: JsonPropertyName("scpTVA")] bool ScpTva
    );

    private sealed record AnafAdresaSediuSocialRaw(
        [property: JsonPropertyName("sdenumire_Strada")]     string? Strada,
        [property: JsonPropertyName("snumar_Strada")]        string? Numar,
        [property: JsonPropertyName("sdenumire_Localitate")] string? Localitate,
        [property: JsonPropertyName("sdenumire_Judet")]      string? Judet,
        [property: JsonPropertyName("scod_Postal")]          string? CodPostal,
        [property: JsonPropertyName("stara")]                string? Tara
    );
}
