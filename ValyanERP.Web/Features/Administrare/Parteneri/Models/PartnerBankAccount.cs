using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Models;

/// <summary>
/// Cont bancar al unui partener.
/// </summary>
public class PartnerBankAccount
{
    /// <summary>Identificator unic.</summary>
    public Guid Id { get; set; }

    /// <summary>ID-ul partenerului.</summary>
    public Guid PartnerId { get; set; }

    /// <summary>IBAN (format internațional, max 34 caractere).</summary>
    [Required, StringLength(34)]
    public string IBAN { get; set; } = string.Empty;

    /// <summary>Codul SWIFT/BIC.</summary>
    [StringLength(11)]
    public string? BIC { get; set; }

    /// <summary>Numele băncii.</summary>
    [Required, StringLength(100)]
    public string NumeBanca { get; set; } = string.Empty;

    /// <summary>Sucursala băncii.</summary>
    [StringLength(100)]
    public string? Sucursala { get; set; }

    /// <summary>Moneda contului (ISO 4217).</summary>
    [StringLength(3)]
    public string Moneda { get; set; } = "RON";

    /// <summary>Titularul contului (dacă diferă de partener).</summary>
    [StringLength(200)]
    public string? TitularCont { get; set; }

    /// <summary>Este contul principal pentru plăți.</summary>
    public bool EstePrincipal { get; set; }

    /// <summary>Observații.</summary>
    [StringLength(500)]
    public string? Observatii { get; set; }

    /// <summary>Înregistrare activă.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Ordine de sortare.</summary>
    public int SortOrder { get; set; }

    /// <summary>Data creării.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>ID utilizator care a creat.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>Data ultimei modificări.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>ID utilizator care a modificat.</summary>
    public Guid? UpdatedBy { get; set; }

    /// <summary>IBAN formatat pentru afișare (cu spații).</summary>
    public string IBANFormatat => FormatIBAN(IBAN);

    private static string FormatIBAN(string iban)
    {
        if (string.IsNullOrEmpty(iban)) return string.Empty;
        var clean = iban.Replace(" ", "").ToUpperInvariant();
        return string.Join(" ", Enumerable.Range(0, (clean.Length + 3) / 4)
            .Select(i => clean.Substring(i * 4, Math.Min(4, clean.Length - i * 4))));
    }
}
