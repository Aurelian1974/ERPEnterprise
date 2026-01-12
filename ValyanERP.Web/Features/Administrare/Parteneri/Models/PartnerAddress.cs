using System.ComponentModel.DataAnnotations;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Models;

/// <summary>
/// Adresa unui partener (sediu, corespondență, livrare, facturare).
/// </summary>
public class PartnerAddress
{
    /// <summary>Identificator unic.</summary>
    public Guid Id { get; set; }

    /// <summary>ID-ul partenerului.</summary>
    public Guid PartnerId { get; set; }

    /// <summary>Tipul adresei.</summary>
    public TipAdresa TipAdresa { get; set; } = TipAdresa.Sediu;

    /// <summary>Denumire opțională (ex: "Depozit București").</summary>
    [StringLength(100)]
    public string? Denumire { get; set; }

    /// <summary>Adresa completă (stradă, nr, bloc, scara, ap).</summary>
    [Required, StringLength(500)]
    public string Adresa { get; set; } = string.Empty;

    /// <summary>Localitatea.</summary>
    [Required, StringLength(100)]
    public string Localitate { get; set; } = string.Empty;

    /// <summary>Județul.</summary>
    [Required, StringLength(50)]
    public string Judet { get; set; } = string.Empty;

    /// <summary>Codul poștal.</summary>
    [StringLength(10)]
    public string? CodPostal { get; set; }

    /// <summary>Țara.</summary>
    [StringLength(50)]
    public string Tara { get; set; } = "România";

    /// <summary>Codul ISO al țării.</summary>
    [StringLength(3)]
    public string CodTaraISO { get; set; } = "RO";

    /// <summary>Latitudine GPS.</summary>
    public decimal? Latitudine { get; set; }

    /// <summary>Longitudine GPS.</summary>
    public decimal? Longitudine { get; set; }

    /// <summary>Telefon la adresă.</summary>
    [StringLength(20)]
    public string? Telefon { get; set; }

    /// <summary>Email la adresă.</summary>
    [StringLength(256), EmailAddress]
    public string? Email { get; set; }

    /// <summary>Persoana de contact la adresă.</summary>
    [StringLength(200)]
    public string? PersoanaContact { get; set; }

    /// <summary>Este adresa principală.</summary>
    public bool EstePrincipala { get; set; }

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

    /// <summary>Adresa formatată pentru afișare.</summary>
    public string AdresaCompleta => $"{Adresa}, {Localitate}, {Judet}{(string.IsNullOrEmpty(CodPostal) ? "" : $", {CodPostal}")}, {Tara}";
}
