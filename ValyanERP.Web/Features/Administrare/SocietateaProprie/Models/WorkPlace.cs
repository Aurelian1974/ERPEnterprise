using System.ComponentModel.DataAnnotations;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models.Enums;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

/// <summary>
/// Reprezintă un punct de lucru al unei companii.
/// </summary>
public class WorkPlace
{
    /// <summary>
    /// Identificator unic.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// ID-ul companiei căreia îi aparține.
    /// </summary>
    [Required(ErrorMessage = "Compania este obligatorie")]
    public Guid CompanyId { get; set; }

    /// <summary>
    /// Codul ONRC al punctului de lucru.
    /// </summary>
    [StringLength(50, ErrorMessage = "Codul ONRC nu poate depăși 50 caractere")]
    public string? CodONRC { get; set; }

    /// <summary>
    /// Denumirea punctului de lucru.
    /// </summary>
    [Required(ErrorMessage = "Denumirea este obligatorie")]
    [StringLength(200, ErrorMessage = "Denumirea nu poate depăși 200 caractere")]
    public string Denumire { get; set; } = string.Empty;

    /// <summary>
    /// Tipul punctului de lucru.
    /// </summary>
    public TipPunctLucru TipPunctLucru { get; set; } = TipPunctLucru.SediuSocial;

    /// <summary>
    /// Regimul juridic al spațiului.
    /// </summary>
    public RegimJuridic RegimJuridic { get; set; } = RegimJuridic.Proprietate;

    /// <summary>
    /// Adresa completă.
    /// </summary>
    [Required(ErrorMessage = "Adresa este obligatorie")]
    [StringLength(500, ErrorMessage = "Adresa nu poate depăși 500 caractere")]
    public string Adresa { get; set; } = string.Empty;

    /// <summary>
    /// Localitatea.
    /// </summary>
    [Required(ErrorMessage = "Localitatea este obligatorie")]
    [StringLength(100, ErrorMessage = "Localitatea nu poate depăși 100 caractere")]
    public string Localitate { get; set; } = string.Empty;

    /// <summary>
    /// Județul.
    /// </summary>
    [Required(ErrorMessage = "Județul este obligatoriu")]
    [StringLength(50, ErrorMessage = "Județul nu poate depăși 50 caractere")]
    public string Judet { get; set; } = string.Empty;

    /// <summary>
    /// Codul poștal.
    /// </summary>
    [StringLength(10, ErrorMessage = "Codul poștal nu poate depăși 10 caractere")]
    public string? CodPostal { get; set; }

    /// <summary>
    /// Țara.
    /// </summary>
    [StringLength(50)]
    public string Tara { get; set; } = "România";

    /// <summary>
    /// Numărul de telefon.
    /// </summary>
    [StringLength(20)]
    [Phone(ErrorMessage = "Numărul de telefon nu este valid")]
    public string? Telefon { get; set; }

    /// <summary>
    /// Adresa de email.
    /// </summary>
    [StringLength(100)]
    [EmailAddress(ErrorMessage = "Adresa de email nu este validă")]
    public string? Email { get; set; }

    /// <summary>
    /// Marchează acest punct de lucru ca sediu social al companiei.
    /// </summary>
    public bool EsteSediuSocial { get; set; }

    /// <summary>
    /// Starea activă.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Data înregistrării la ONRC.
    /// </summary>
    public DateTime? DataInregistrare { get; set; }

    /// <summary>
    /// Data radierii de la ONRC.
    /// </summary>
    public DateTime? DataRadiere { get; set; }

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

    // Navigation properties (populate from stored procedure)

    /// <summary>
    /// Denumirea companiei.
    /// </summary>
    public string? CompanyName { get; set; }

    /// <summary>
    /// CUI-ul companiei.
    /// </summary>
    public string? CompanyCUI { get; set; }

    /// <summary>
    /// Numărul de locații asociate.
    /// </summary>
    public int LocationCount { get; set; }

    /// <summary>
    /// Returnează adresa completă formatată.
    /// </summary>
    public string AdresaCompleta => $"{Adresa}, {Localitate}, {Judet}, {Tara}";

    /// <summary>
    /// Returnează descrierea tipului de punct de lucru.
    /// </summary>
    public string TipPunctLucruDescriere => TipPunctLucru switch
    {
        TipPunctLucru.SediuSocial => "Sediu Social",
        TipPunctLucru.Sucursala => "Sucursală",
        TipPunctLucru.Agentie => "Agenție",
        TipPunctLucru.PunctLucru => "Punct de Lucru",
        _ => "Necunoscut"
    };

    /// <summary>
    /// Returnează descrierea regimului juridic.
    /// </summary>
    public string RegimJuridicDescriere => RegimJuridic switch
    {
        RegimJuridic.Proprietate => "Proprietate",
        RegimJuridic.Inchiriere => "Închiriere",
        RegimJuridic.Comodat => "Comodat",
        RegimJuridic.Leasing => "Leasing",
        _ => "Necunoscut"
    };
}
