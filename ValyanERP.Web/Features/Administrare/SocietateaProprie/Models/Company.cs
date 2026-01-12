using System.ComponentModel.DataAnnotations;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models.Enums;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

/// <summary>
/// Reprezintă o companie (persoană juridică).
/// </summary>
public class Company
{
    /// <summary>
    /// Identificator unic.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// ID-ul grupului din care face parte (opțional).
    /// </summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// ID-ul companiei părinte (pentru structuri de holding).
    /// </summary>
    public Guid? ParentCompanyId { get; set; }

    /// <summary>
    /// Codul unic de identificare fiscală.
    /// </summary>
    [Required(ErrorMessage = "CUI este obligatoriu")]
    [StringLength(20, ErrorMessage = "CUI nu poate depăși 20 caractere")]
    public string CUI { get; set; } = string.Empty;

    /// <summary>
    /// Numărul de înregistrare la Registrul Comerțului.
    /// </summary>
    [StringLength(50, ErrorMessage = "Reg.Com. nu poate depăși 50 caractere")]
    public string? RegCom { get; set; }

    /// <summary>
    /// Denumirea oficială a companiei.
    /// </summary>
    [Required(ErrorMessage = "Denumirea este obligatorie")]
    [StringLength(200, ErrorMessage = "Denumirea nu poate depăși 200 caractere")]
    public string Denumire { get; set; } = string.Empty;

    /// <summary>
    /// Denumirea scurtă (prescurtare).
    /// </summary>
    [StringLength(50, ErrorMessage = "Denumirea scurtă nu poate depăși 50 caractere")]
    public string? DenumireScurta { get; set; }

    /// <summary>
    /// Tipul companiei în structura de grup.
    /// </summary>
    public TipCompanie TipCompanie { get; set; } = TipCompanie.Independent;

    /// <summary>
    /// Procentul de deținere (pentru subsidiary).
    /// </summary>
    [Range(0, 100, ErrorMessage = "Procentul trebuie să fie între 0 și 100")]
    public decimal? ProcentDetinere { get; set; }

    /// <summary>
    /// Capitalul social.
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Capitalul social trebuie să fie pozitiv")]
    public decimal? CapitalSocial { get; set; }

    /// <summary>
    /// Numărul de telefon al companiei.
    /// </summary>
    [StringLength(20)]
    [Phone(ErrorMessage = "Numărul de telefon nu este valid")]
    public string? Telefon { get; set; }

    /// <summary>
    /// Adresa de email a companiei.
    /// </summary>
    [StringLength(100)]
    [EmailAddress(ErrorMessage = "Adresa de email nu este validă")]
    public string? Email { get; set; }

    /// <summary>
    /// Website-ul companiei.
    /// </summary>
    [StringLength(200)]
    public string? Website { get; set; }

    /// <summary>
    /// URL-ul logo-ului companiei.
    /// </summary>
    [StringLength(500)]
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Marchează compania principală din grup.
    /// </summary>
    public bool IsPrincipal { get; set; }

    /// <summary>
    /// Starea activă a companiei.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Ordinea de sortare.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Data creării.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Utilizatorul care a creat înregistrarea.
    /// </summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>
    /// Data ultimei modificări.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Utilizatorul care a modificat ultima dată.
    /// </summary>
    public Guid? UpdatedBy { get; set; }

    // Navigation properties (populate from View vw_CompanyWithSediuSocial)

    /// <summary>
    /// Denumirea grupului din care face parte.
    /// </summary>
    public string? GroupName { get; set; }

    /// <summary>
    /// Denumirea companiei părinte.
    /// </summary>
    public string? ParentCompanyName { get; set; }

    /// <summary>
    /// Numărul de puncte de lucru.
    /// </summary>
    public int WorkPlaceCount { get; set; }

    /// <summary>
    /// Numărul de locații.
    /// </summary>
    public int LocationCount { get; set; }

    // Sediu Social (from View - read-only)

    /// <summary>
    /// ID-ul punctului de lucru care este sediu social.
    /// </summary>
    public Guid? SediuSocialId { get; set; }

    /// <summary>
    /// Adresa sediului social.
    /// </summary>
    public string? SediuSocialAdresa { get; set; }

    /// <summary>
    /// Localitatea sediului social.
    /// </summary>
    public string? SediuSocialLocalitate { get; set; }

    /// <summary>
    /// Județul sediului social.
    /// </summary>
    public string? SediuSocialJudet { get; set; }

    /// <summary>
    /// Codul poștal al sediului social.
    /// </summary>
    public string? SediuSocialCodPostal { get; set; }

    /// <summary>
    /// Țara sediului social.
    /// </summary>
    public string? SediuSocialTara { get; set; }

    /// <summary>
    /// Regimul juridic al sediului social.
    /// </summary>
    public RegimJuridic? SediuSocialRegimJuridic { get; set; }

    /// <summary>
    /// Codul ONRC al sediului social.
    /// </summary>
    public string? SediuSocialCodONRC { get; set; }

    /// <summary>
    /// Returnează adresa completă a sediului social.
    /// </summary>
    public string SediuSocialComplet => string.IsNullOrEmpty(SediuSocialAdresa) 
        ? "Nedefinit" 
        : $"{SediuSocialAdresa}, {SediuSocialLocalitate}, {SediuSocialJudet}";
}
