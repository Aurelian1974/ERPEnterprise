using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ValyanERP.Web.Features.Administrare.Parteneri.Models;
using ValyanERP.Web.Features.Administrare.Parteneri.Repositories;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Services;

/// <summary>
/// Excepție aruncată când CUI-ul nu este găsit în baza ANAF.
/// </summary>
public class CuiNotFoundException : Exception
{
    public CuiNotFoundException(string message) : base(message) { }
}

/// <summary>
/// Implementare serviciu verificare ANAF.
/// API v9: https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9/tva
/// Documentație oficială ANAF - API sincron pentru verificare TVA.
/// </summary>
public class AnafVerificationService : IAnafVerificationService
{
    private readonly HttpClient _httpClient;
    private readonly IPartnerRepository _partnerRepository;
    private readonly ILogger<AnafVerificationService> _logger;

    // API URLs ANAF - endpoint public fără autentificare
    // API v9 este SINCRON: răspunsul vine direct la POST
    private const string ANAF_API_BASE = "https://webservicesp.anaf.ro";
    private const string ANAF_TVA_ENDPOINT = "/api/PlatitorTvaRest/v9/tva";

    // Cache duration în ore (24h default)
    private const int CACHE_DURATION_HOURS = 24;

    // Max CUI-uri per request (limita ANAF = 100)
    private const int MAX_BATCH_SIZE = 100;

    public AnafVerificationService(
        HttpClient httpClient,
        IPartnerRepository partnerRepository,
        ILogger<AnafVerificationService> logger)
    {
        _httpClient = httpClient;
        _partnerRepository = partnerRepository;
        _logger = logger;

        // Configurare HttpClient - nu seta BaseAddress aici, o setăm în request
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <inheritdoc />
    public async Task<AnafVerificationResult> VerifyAsync(string cui, bool useCache = true)
    {
        try
        {
            // Normalizează CUI (elimină RO, spații, etc.)
            cui = NormalizeCui(cui);

            if (string.IsNullOrWhiteSpace(cui))
            {
                return AnafVerificationResult.Fail(cui, "CUI invalid sau gol.");
            }

            // Verifică cache
            if (useCache)
            {
                var cached = await _partnerRepository.GetAnafCacheAsync(cui);
                if (cached != null && cached.ExpiresAt > DateTime.UtcNow)
                {
                    _logger.LogDebug("ANAF cache hit pentru CUI={CUI}", cui);
                    return AnafVerificationResult.Ok(cached, AnafDataSource.Cache);
                }
            }

            // Apelează API ANAF
            var apiResult = await CallAnafApiAsync(cui);
            
            if (apiResult == null)
            {
                return AnafVerificationResult.Fail(cui, "Nu s-a putut obține răspuns de la ANAF.");
            }

            // Salvează în cache
            apiResult.ExpiresAt = DateTime.UtcNow.AddHours(CACHE_DURATION_HOURS);
            await _partnerRepository.SaveAnafCacheAsync(apiResult);

            _logger.LogInformation("ANAF verificare reușită pentru CUI={CUI}", cui);
            return AnafVerificationResult.Ok(apiResult, AnafDataSource.Api);
        }
        catch (CuiNotFoundException ex)
        {
            // CUI-ul nu există în baza ANAF - mesaj clar pentru utilizator
            _logger.LogWarning("CUI negăsit în ANAF: {Message}", ex.Message);
            return AnafVerificationResult.Fail(cui, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Eroare conexiune ANAF pentru CUI={CUI}", cui);
            return AnafVerificationResult.Fail(cui, $"Eroare conexiune ANAF: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout ANAF pentru CUI={CUI}", cui);
            return AnafVerificationResult.Fail(cui, "Timeout la conexiunea cu ANAF.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare neașteptată la verificare ANAF pentru CUI={CUI}", cui);
            return AnafVerificationResult.Fail(cui, $"Eroare: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<AnafVerificationResult> VerifyAndUpdatePartnerAsync(Guid partnerId, Guid? updatedBy = null)
    {
        try
        {
            // Obține partenerul
            var partner = await _partnerRepository.GetByIdAsync(partnerId);
            if (partner == null)
            {
                return AnafVerificationResult.Fail(string.Empty, "Partenerul nu a fost găsit.");
            }

            // Determină CUI-ul de verificat
            var cui = partner.CUI ?? partner.CIF;
            if (string.IsNullOrWhiteSpace(cui))
            {
                return AnafVerificationResult.Fail(string.Empty, "Partenerul nu are CUI/CIF setat.");
            }

            // Verifică în ANAF
            var result = await VerifyAsync(cui, useCache: false);

            if (result.Success && result.Data != null)
            {
                // Actualizează TOATE datele partenerului din ANAF (denumire, telefon, statusuri)
                await _partnerRepository.UpdateFromAnafAsync(
                    partnerId,
                    result.Data,
                    updatedBy
                );

                // Actualizează/creează adresa de tip Sediu din ANAF
                var addressId = await _partnerRepository.UpsertSediuAddressFromAnafAsync(
                    partnerId,
                    result.Data,
                    updatedBy
                );

                _logger.LogInformation(
                    "Partner {PartnerId} actualizat din ANAF. Denumire={Denumire}, TVA={TVA}, Inactiv={Inactiv}, AdresaUpdated={AdresaUpdated}",
                    partnerId, result.Data.Denumire, result.Data.ScpTVA, result.Data.StatusInactivi, addressId != null);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la verificare și actualizare partener {PartnerId}", partnerId);
            return AnafVerificationResult.Fail(string.Empty, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, AnafVerificationResult>> VerifyBatchAsync(IEnumerable<string> cuiList)
    {
        var results = new Dictionary<string, AnafVerificationResult>();
        var cuiArray = cuiList.Select(NormalizeCui).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();

        if (!cuiArray.Any())
        {
            return results;
        }

        // Împarte în batch-uri de max 500
        var batches = cuiArray.Chunk(MAX_BATCH_SIZE);

        foreach (var batch in batches)
        {
            try
            {
                var batchResults = await CallAnafApiBatchAsync(batch.ToList());
                foreach (var kvp in batchResults)
                {
                    results[kvp.Key] = kvp.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Eroare la batch ANAF pentru {Count} CUI-uri", batch.Count());
                foreach (var cui in batch)
                {
                    results[cui] = AnafVerificationResult.Fail(cui, ex.Message);
                }
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredCacheAsync()
    {
        try
        {
            return await _partnerRepository.CleanupExpiredAnafCacheAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Eroare la curățarea cache-ului ANAF");
            return 0;
        }
    }

    #region Private Methods

    /// <summary>
    /// Normalizează CUI-ul (elimină RO, spații, caractere non-numerice).
    /// </summary>
    private static string NormalizeCui(string cui)
    {
        if (string.IsNullOrWhiteSpace(cui))
            return string.Empty;

        // Elimină "RO" prefix și spații
        cui = cui.Trim().ToUpperInvariant();
        if (cui.StartsWith("RO"))
            cui = cui[2..];

        // Păstrează doar cifrele
        return new string(cui.Where(char.IsDigit).ToArray());
    }

    /// <summary>
    /// Apelează API-ul ANAF pentru un singur CUI.
    /// API v9 este sincron: răspunsul vine direct la POST.
    /// </summary>
    private async Task<AnafVerificationCache?> CallAnafApiAsync(string cui)
    {
        if (!long.TryParse(cui.Trim(), out var cuiNumeric))
        {
            _logger.LogWarning("CUI invalid (nu poate fi convertit la număr): {CUI}", cui);
            return null;
        }
        
        var request = new List<AnafRequest>
        {
            new() { Cui = cuiNumeric, Data = DateTime.Today.ToString("yyyy-MM-dd") }
        };

        var fullUrl = ANAF_API_BASE + ANAF_TVA_ENDPOINT;
        _logger.LogInformation("ANAF API v9 POST: {Url} for CUI={CUI}", fullUrl, cui);
        
        var response = await _httpClient.PostAsJsonAsync(fullUrl, request);
        var responseContent = await response.Content.ReadAsStringAsync();
        
        // ANAF returnează HTTP 404 când CUI-ul nu este găsit, dar body-ul conține JSON valid cu notFound
        if (!response.IsSuccessStatusCode)
        {
            // Verificăm dacă este cazul "CUI negăsit" (HTTP 404 cu notFound în body)
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrEmpty(responseContent))
            {
                try
                {
                    var notFoundResponse = JsonSerializer.Deserialize<AnafResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (notFoundResponse?.Notfound != null && notFoundResponse.Notfound.Count > 0)
                    {
                        _logger.LogInformation("ANAF: CUI={CUI} nu a fost găsit în baza ANAF (notFound)", cui);
                        // Returnăm null cu un semnal special că CUI-ul nu există
                        throw new CuiNotFoundException($"Nu a fost găsită nicio societate cu CUI: {cui}");
                    }
                }
                catch (JsonException)
                {
                    // Nu este JSON valid, continuă cu eroarea standard
                }
                catch (CuiNotFoundException)
                {
                    throw; // Re-aruncă excepția CuiNotFoundException
                }
            }
            
            _logger.LogError("ANAF API returned {StatusCode}: {Content}", response.StatusCode, responseContent);
            throw new HttpRequestException($"ANAF API a returnat eroare {(int)response.StatusCode}. Serviciul ANAF poate fi temporar indisponibil.");
        }

        _logger.LogDebug("ANAF Response: {Response}", responseContent);
        
        var anafResponse = JsonSerializer.Deserialize<AnafResponse>(responseContent, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        // API v9 nu returnează cod/message la nivel root - are direct found/notFound
        if (anafResponse == null)
        {
            _logger.LogError("ANAF API response deserializare eșuată");
            throw new HttpRequestException("ANAF API eroare: Răspuns invalid (deserializare eșuată)");
        }
        
        if (anafResponse.Found != null && anafResponse.Found.Count > 0)
        {
            return MapToCache(anafResponse.Found[0], cui);
        }
        
        if (anafResponse.Notfound != null && anafResponse.Notfound.Count > 0)
        {
            _logger.LogWarning("CUI={CUI} nu a fost găsit în baza ANAF", cui);
            return null;
        }
        
        _logger.LogWarning("ANAF API răspuns gol pentru CUI={CUI}", cui);
        return null;
    }

    /// <summary>
    /// Apelează API-ul ANAF pentru mai multe CUI-uri (batch).
    /// API v9 este sincron: răspunsul vine direct. Max 100 CUI-uri per request.
    /// </summary>
    private async Task<Dictionary<string, AnafVerificationResult>> CallAnafApiBatchAsync(List<string> cuiList)
    {
        var results = new Dictionary<string, AnafVerificationResult>();
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // Convertim CUI-urile la numere
        var request = new List<AnafRequest>();
        foreach (var cui in cuiList)
        {
            if (long.TryParse(cui.Trim(), out var cuiNumeric))
            {
                request.Add(new AnafRequest { Cui = cuiNumeric, Data = today });
            }
            else
            {
                _logger.LogWarning("CUI invalid în batch (nu poate fi convertit la număr): {CUI}", cui);
                results[cui] = AnafVerificationResult.Fail(cui, "CUI invalid (format incorect).");
            }
        }

        if (request.Count == 0)
        {
            _logger.LogWarning("Niciun CUI valid în batch pentru verificare ANAF");
            return results;
        }

        var fullUrl = ANAF_API_BASE + ANAF_TVA_ENDPOINT;
        _logger.LogInformation("ANAF API v9 batch POST: {Url} for {Count} CUI-uri", fullUrl, request.Count);
        
        var response = await _httpClient.PostAsJsonAsync(fullUrl, request);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("ANAF API batch returned {StatusCode}: {Content}", response.StatusCode, errorContent);
            throw new HttpRequestException($"ANAF API eroare: {response.StatusCode}");
        }
        
        var responseContent = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("ANAF batch response: {Content}", responseContent);
        
        var anafResponse = JsonSerializer.Deserialize<AnafResponse>(responseContent, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });
        
        // API v9 nu returnează cod/message la nivel root
        if (anafResponse == null)
        {
            _logger.LogError("ANAF API batch deserializare eșuată");
            throw new HttpRequestException("ANAF API eroare: Răspuns invalid (deserializare eșuată)");
        }

        // Procesează rezultatele găsite
        if (anafResponse.Found != null)
        {
            foreach (var found in anafResponse.Found)
            {
                var cuiFromResponse = found.DateGenerale?.Cui.ToString() ?? string.Empty;
                var cache = MapToCache(found, cuiFromResponse);
                if (cache != null)
                {
                    cache.ExpiresAt = DateTime.UtcNow.AddHours(CACHE_DURATION_HOURS);
                    await _partnerRepository.SaveAnafCacheAsync(cache);
                    results[cuiFromResponse] = AnafVerificationResult.Ok(cache, AnafDataSource.Api);
                }
            }
        }

        // Marchează cele negăsite
        if (anafResponse?.Notfound != null)
        {
            foreach (var notfoundCui in anafResponse.Notfound)
            {
                var cui = notfoundCui.ToString();
                results[cui] = AnafVerificationResult.Fail(cui, $"Nu a fost găsită nicio societate cu CUI: {cui}");
            }
        }

        return results;
    }

    /// <summary>
    /// Mapează răspunsul ANAF la modelul nostru de cache.
    /// </summary>
    private static AnafVerificationCache? MapToCache(AnafFoundItem item, string cui)
    {
        if (item.DateGenerale == null)
            return null;

        var dg = item.DateGenerale;
        var tva = item.InregistrareScopTva;
        var split = item.InregistrareSplitTva;
        var inactiv = item.StareInactiv;
        var sediu = item.AdresaSediuSocial;

        // Data înregistrare TVA vine din prima perioadă TVA
        var primaPerioada = tva?.PerioadeTVA?.FirstOrDefault();

        return new AnafVerificationCache
        {
            Id = Guid.NewGuid(),
            CUI = cui,
            Denumire = dg.Denumire,
            Adresa = dg.Adresa,
            NrRegCom = dg.NrRegCom,
            CodPostal = dg.CodPostal,
            Telefon = dg.Telefon,
            Fax = dg.Fax,

            // Adresa sediu social (structurată)
            SediuLocalitate = sediu?.DenumireLocalitate,
            SediuStrada = sediu?.DenumireStrada,
            SediuNumar = sediu?.NumarStrada,
            SediuJudet = sediu?.DenumireJudet,
            SediuCodJudetAuto = sediu?.CodJudetAuto,
            SediuDetalii = sediu?.DetaliiAdresa,
            SediuCodPostal = sediu?.CodPostal,
            SediuTara = string.IsNullOrWhiteSpace(sediu?.Tara) ? "România" : sediu.Tara,

            // Status TVA - data înregistrare din prima perioadă
            ScpTVA = tva?.ScpTVA ?? false,
            DataInregistrareTVA = ParseDate(primaPerioada?.DataInceput),
            DataAnulareTVA = ParseDate(primaPerioada?.DataSfarsit),
            StatusTVA = tva?.ScpTVA == true ? "Înregistrat" : "Neînregistrat",
            StatusTVAMessage = primaPerioada?.Mesaj,

            // Split TVA
            StatusSplitTVA = split?.StatusSplitTVA ?? false,
            DataInceputSplitTVA = ParseDate(split?.DataInceputSplitTVA),
            DataAnulareSplitTVA = ParseDate(split?.DataAnulareSplitTVA),

            // Inactivi
            StatusInactivi = inactiv?.StatusInactivi ?? false,
            DataInactivare = ParseDate(inactiv?.DataInactivare),
            DataReactivare = ParseDate(inactiv?.DataReactivare),
            StatusInactiviMessage = null, // Nu mai există în răspunsul ANAF

            // RO e-Factura
            StatusRoEfactura = item.InregistrareRoEfactura?.StatusRoEfactura ?? false,
            DataInceputRoEfactura = ParseDate(item.InregistrareRoEfactura?.DataInceputRoEfactura),

            // Insolventa
            StatusInsolventa = item.StareInsolventa?.StatusInsolventa ?? false,
            DataInsolventa = ParseDate(item.StareInsolventa?.DataInsolventa),

            // Metadata
            DataInterogare = DateTime.Today,
            VerifiedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return null;

        if (DateTime.TryParse(dateStr, out var date))
            return date;

        return null;
    }

    #endregion
}

#region ANAF API DTOs

/// <summary>Request pentru API ANAF. CUI trebuie trimis ca număr!</summary>
internal class AnafRequest
{
    [JsonPropertyName("cui")]
    public long Cui { get; set; }

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
}

/// <summary>Response de la API ANAF v9 (sincron).</summary>
internal class AnafResponse
{
    [JsonPropertyName("cod")]
    public int Cod { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("found")]
    public List<AnafFoundItem>? Found { get; set; }

    [JsonPropertyName("notFound")]
    public List<long>? Notfound { get; set; }
}

/// <summary>Element găsit în răspunsul ANAF.</summary>
internal class AnafFoundItem
{
    [JsonPropertyName("date_generale")]
    public AnafDateGenerale? DateGenerale { get; set; }

    [JsonPropertyName("inregistrare_scop_Tva")]
    public AnafTvaInfo? InregistrareScopTva { get; set; }

    [JsonPropertyName("inregistrare_RTVAI")]
    public AnafRtvaiInfo? InregistrareRtvai { get; set; }

    [JsonPropertyName("inregistrare_SplitTVA")]
    public AnafSplitTvaInfo? InregistrareSplitTva { get; set; }

    [JsonPropertyName("stare_inactiv")]
    public AnafInactivInfo? StareInactiv { get; set; }

    [JsonPropertyName("inregistrare_RO_e_Factura")]
    public AnafRoEfacturaInfo? InregistrareRoEfactura { get; set; }

    [JsonPropertyName("stare_insolventa")]
    public AnafInsolventaInfo? StareInsolventa { get; set; }

    [JsonPropertyName("adresa_sediu_social")]
    public AnafAdresaSediuSocial? AdresaSediuSocial { get; set; }

    [JsonPropertyName("adresa_domiciliu_fiscal")]
    public AnafAdresaDomiciliuFiscal? AdresaDomiciliuFiscal { get; set; }
}

/// <summary>Element negăsit.</summary>
internal class AnafNotFoundItem
{
    [JsonPropertyName("cui")]
    public object? Cui { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }
}

/// <summary>Date generale firmă.</summary>
internal class AnafDateGenerale
{
    [JsonPropertyName("cui")]
    public long Cui { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("denumire")]
    public string? Denumire { get; set; }

    [JsonPropertyName("adresa")]
    public string? Adresa { get; set; }

    [JsonPropertyName("nrRegCom")]
    public string? NrRegCom { get; set; }

    [JsonPropertyName("telefon")]
    public string? Telefon { get; set; }

    [JsonPropertyName("fax")]
    public string? Fax { get; set; }

    [JsonPropertyName("codPostal")]
    public string? CodPostal { get; set; }

    [JsonPropertyName("act")]
    public string? Act { get; set; }

    [JsonPropertyName("stare_inregistrare")]
    public string? StareInregistrare { get; set; }

    [JsonPropertyName("data_inregistrare")]
    public string? DataInregistrare { get; set; }

    [JsonPropertyName("cod_CAEN")]
    public string? CodCAEN { get; set; }

    [JsonPropertyName("iban")]
    public string? IBAN { get; set; }

    [JsonPropertyName("statusRO_e_Factura")]
    public bool StatusRoEfactura { get; set; }

    [JsonPropertyName("organFiscalCompetent")]
    public string? OrganFiscalCompetent { get; set; }

    [JsonPropertyName("forma_de_proprietate")]
    public string? FormaProprietate { get; set; }

    [JsonPropertyName("forma_organizare")]
    public string? FormaOrganizare { get; set; }

    [JsonPropertyName("forma_juridica")]
    public string? FormaJuridica { get; set; }
}

/// <summary>Info TVA.</summary>
internal class AnafTvaInfo
{
    [JsonPropertyName("scpTVA")]
    public bool ScpTVA { get; set; }

    [JsonPropertyName("perioade_TVA")]
    public List<AnafPerioadaTva>? PerioadeTVA { get; set; }

    [JsonPropertyName("dataInregistrareTVA")]
    public string? DataInregistrareTVA { get; set; }

    [JsonPropertyName("dataAnulareTVA")]
    public string? DataAnulareTVA { get; set; }

    [JsonPropertyName("statusTVA")]
    public object? StatusTVA { get; set; }

    [JsonPropertyName("statusTVA_message")]
    public string? StatusTVAMessage { get; set; }
}

/// <summary>Perioadă TVA.</summary>
internal class AnafPerioadaTva
{
    [JsonPropertyName("data_inceput_ScpTVA")]
    public string? DataInceput { get; set; }

    [JsonPropertyName("data_sfarsit_ScpTVA")]
    public string? DataSfarsit { get; set; }

    [JsonPropertyName("data_anul_imp_ScpTVA")]
    public string? DataAnulImp { get; set; }

    [JsonPropertyName("mesaj_ScpTVA")]
    public string? Mesaj { get; set; }
}

/// <summary>Info Split TVA.</summary>
internal class AnafSplitTvaInfo
{
    [JsonPropertyName("statusSplitTVA")]
    public bool StatusSplitTVA { get; set; }

    [JsonPropertyName("dataInceputSplitTVA")]
    public string? DataInceputSplitTVA { get; set; }

    [JsonPropertyName("dataAnulareSplitTVA")]
    public string? DataAnulareSplitTVA { get; set; }
}

/// <summary>Info RTVAI (TVA la încasare).</summary>
internal class AnafRtvaiInfo
{
    [JsonPropertyName("statusTvaIncasare")]
    public bool StatusTvaIncasare { get; set; }

    [JsonPropertyName("dataInceputTvaInc")]
    public string? DataInceputTvaInc { get; set; }

    [JsonPropertyName("dataSfarsitTvaInc")]
    public string? DataSfarsitTvaInc { get; set; }

    [JsonPropertyName("dataActualizareTvaInc")]
    public string? DataActualizareTvaInc { get; set; }

    [JsonPropertyName("dataPublicareTvaInc")]
    public string? DataPublicareTvaInc { get; set; }

    [JsonPropertyName("tipActTvaInc")]
    public string? TipActTvaInc { get; set; }
}

/// <summary>Info stare inactiv fiscal.</summary>
internal class AnafInactivInfo
{
    [JsonPropertyName("statusInactivi")]
    public bool StatusInactivi { get; set; }

    [JsonPropertyName("dataInactivare")]
    public string? DataInactivare { get; set; }

    [JsonPropertyName("dataReactivare")]
    public string? DataReactivare { get; set; }

    [JsonPropertyName("dataPublicare")]
    public string? DataPublicare { get; set; }

    [JsonPropertyName("dataRadiere")]
    public string? DataRadiere { get; set; }
}

/// <summary>Info RO e-Factura.</summary>
internal class AnafRoEfacturaInfo
{
    [JsonPropertyName("statusRO_e_Factura")]
    public bool StatusRoEfactura { get; set; }

    [JsonPropertyName("dataInceputRO_e_Factura")]
    public string? DataInceputRoEfactura { get; set; }
}

/// <summary>Info insolvență.</summary>
internal class AnafInsolventaInfo
{
    [JsonPropertyName("stare_insolventa")]
    public bool StatusInsolventa { get; set; }

    [JsonPropertyName("dataInsolventa")]
    public string? DataInsolventa { get; set; }
}

/// <summary>Adresa sediu social din răspunsul ANAF.</summary>
internal class AnafAdresaSediuSocial
{
    [JsonPropertyName("sdenumire_Localitate")]
    public string? DenumireLocalitate { get; set; }

    [JsonPropertyName("sdenumire_Strada")]
    public string? DenumireStrada { get; set; }

    [JsonPropertyName("snumar_Strada")]
    public string? NumarStrada { get; set; }

    [JsonPropertyName("scod_Localitate")]
    public string? CodLocalitate { get; set; }

    [JsonPropertyName("sdenumire_Judet")]
    public string? DenumireJudet { get; set; }

    [JsonPropertyName("scod_Judet")]
    public string? CodJudet { get; set; }

    [JsonPropertyName("scod_JudetAuto")]
    public string? CodJudetAuto { get; set; }

    [JsonPropertyName("sdetalii_Adresa")]
    public string? DetaliiAdresa { get; set; }

    [JsonPropertyName("scod_Postal")]
    public string? CodPostal { get; set; }

    [JsonPropertyName("stara")]
    public string? Tara { get; set; }
}

/// <summary>Adresa domiciliu fiscal din răspunsul ANAF.</summary>
internal class AnafAdresaDomiciliuFiscal
{
    [JsonPropertyName("ddenumire_Localitate")]
    public string? DenumireLocalitate { get; set; }

    [JsonPropertyName("ddenumire_Strada")]
    public string? DenumireStrada { get; set; }

    [JsonPropertyName("dnumar_Strada")]
    public string? NumarStrada { get; set; }

    [JsonPropertyName("dcod_Localitate")]
    public string? CodLocalitate { get; set; }

    [JsonPropertyName("ddenumire_Judet")]
    public string? DenumireJudet { get; set; }

    [JsonPropertyName("dcod_Judet")]
    public string? CodJudet { get; set; }

    [JsonPropertyName("dcod_JudetAuto")]
    public string? CodJudetAuto { get; set; }

    [JsonPropertyName("ddetalii_Adresa")]
    public string? DetaliiAdresa { get; set; }

    [JsonPropertyName("dcod_Postal")]
    public string? CodPostal { get; set; }

    [JsonPropertyName("dtara")]
    public string? Tara { get; set; }
}

#endregion
