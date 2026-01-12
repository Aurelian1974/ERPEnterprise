using ValyanERP.Web.Features.Administrare.Parteneri.Models;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Services;

/// <summary>
/// Serviciu pentru verificarea partenerilor în sistemele ANAF.
/// Suportă: TVA, Split TVA, Inactivi Fiscali, RO e-Factura.
/// </summary>
public interface IAnafVerificationService
{
    /// <summary>
    /// Verifică un CUI în toate sistemele ANAF disponibile.
    /// </summary>
    /// <param name="cui">Codul Unic de Identificare (fără prefixul RO)</param>
    /// <param name="useCache">Folosește cache-ul dacă există și nu este expirat</param>
    /// <returns>Rezultatul verificării cu toate datele ANAF</returns>
    Task<AnafVerificationResult> VerifyAsync(string cui, bool useCache = true);

    /// <summary>
    /// Verifică un partener și actualizează automat statusul în baza de date.
    /// </summary>
    /// <param name="partnerId">ID-ul partenerului</param>
    /// <param name="updatedBy">ID-ul utilizatorului care face actualizarea</param>
    /// <returns>Rezultatul verificării</returns>
    Task<AnafVerificationResult> VerifyAndUpdatePartnerAsync(Guid partnerId, Guid? updatedBy = null);

    /// <summary>
    /// Verifică mai multe CUI-uri în batch (max 500 per request conform ANAF).
    /// </summary>
    /// <param name="cuiList">Lista de CUI-uri de verificat</param>
    /// <returns>Dictionary cu rezultatele pentru fiecare CUI</returns>
    Task<Dictionary<string, AnafVerificationResult>> VerifyBatchAsync(IEnumerable<string> cuiList);

    /// <summary>
    /// Curăță cache-ul expirat.
    /// </summary>
    /// <returns>Numărul de înregistrări șterse</returns>
    Task<int> CleanupExpiredCacheAsync();
}

/// <summary>
/// Rezultatul verificării ANAF pentru un CUI.
/// </summary>
public class AnafVerificationResult
{
    /// <summary>Este verificarea reușită.</summary>
    public bool Success { get; set; }

    /// <summary>Mesaj de eroare dacă verificarea a eșuat.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>CUI-ul verificat.</summary>
    public string CUI { get; set; } = string.Empty;

    /// <summary>Datele complete din cache/API.</summary>
    public AnafVerificationCache? Data { get; set; }

    /// <summary>Sursa datelor: Cache sau API.</summary>
    public AnafDataSource Source { get; set; }

    /// <summary>Data verificării.</summary>
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Creează un rezultat de succes.</summary>
    public static AnafVerificationResult Ok(AnafVerificationCache data, AnafDataSource source) => new()
    {
        Success = true,
        CUI = data.CUI,
        Data = data,
        Source = source,
        VerifiedAt = DateTime.UtcNow
    };

    /// <summary>Creează un rezultat de eroare.</summary>
    public static AnafVerificationResult Fail(string cui, string error) => new()
    {
        Success = false,
        CUI = cui,
        ErrorMessage = error,
        VerifiedAt = DateTime.UtcNow
    };
}

/// <summary>Sursa datelor verificării.</summary>
public enum AnafDataSource
{
    /// <summary>Date din cache local.</summary>
    Cache,
    /// <summary>Date fresh de la API ANAF.</summary>
    Api
}
