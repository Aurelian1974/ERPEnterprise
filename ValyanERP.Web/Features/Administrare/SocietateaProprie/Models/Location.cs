using System.ComponentModel.DataAnnotations;
using ValyanERP.Web.Features.Administrare.SocietateaProprie.Models.Enums;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

/// <summary>
/// Reprezintă o locație (depozit, magazin, birou, etc.).
/// </summary>
public class Location
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
    /// ID-ul punctului de lucru (null pentru LOC-ONLY).
    /// </summary>
    public Guid? WorkPlaceId { get; set; }

    /// <summary>
    /// Codul intern al locației.
    /// </summary>
    [Required(ErrorMessage = "Codul intern este obligatoriu")]
    [StringLength(50, ErrorMessage = "Codul intern nu poate depăși 50 caractere")]
    public string CodIntern { get; set; } = string.Empty;

    /// <summary>
    /// Denumirea locației.
    /// </summary>
    [Required(ErrorMessage = "Denumirea este obligatorie")]
    [StringLength(200, ErrorMessage = "Denumirea nu poate depăși 200 caractere")]
    public string Denumire { get; set; } = string.Empty;

    /// <summary>
    /// Tipul locației.
    /// </summary>
    public TipLocatie TipLocatie { get; set; } = TipLocatie.Depozit;

    /// <summary>
    /// Adresa locației.
    /// </summary>
    [StringLength(500, ErrorMessage = "Adresa nu poate depăși 500 caractere")]
    public string? Adresa { get; set; }

    /// <summary>
    /// Localitatea.
    /// </summary>
    [StringLength(100, ErrorMessage = "Localitatea nu poate depăși 100 caractere")]
    public string? Localitate { get; set; }

    /// <summary>
    /// Județul.
    /// </summary>
    [StringLength(50, ErrorMessage = "Județul nu poate depăși 50 caractere")]
    public string? Judet { get; set; }

    /// <summary>
    /// Codul poștal.
    /// </summary>
    [StringLength(10, ErrorMessage = "Codul poștal nu poate depăși 10 caractere")]
    public string? CodPostal { get; set; }

    /// <summary>
    /// Regimul juridic al spațiului.
    /// </summary>
    public RegimJuridic RegimJuridic { get; set; } = RegimJuridic.Proprietate;

    /// <summary>
    /// Locația poate gestiona stoc.
    /// </summary>
    public bool HasStock { get; set; }

    /// <summary>
    /// Locația poate emite facturi de vânzare.
    /// </summary>
    public bool CanSell { get; set; }

    /// <summary>
    /// Locația poate recepționa achiziții.
    /// </summary>
    public bool CanPurchase { get; set; }

    /// <summary>
    /// Locația poate produce (manufacturing).
    /// </summary>
    public bool CanManufacture { get; set; }

    /// <summary>
    /// Suprafața în metri pătrați.
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "Suprafața trebuie să fie pozitivă")]
    public decimal? Suprafata { get; set; }

    /// <summary>
    /// Descrierea locației.
    /// </summary>
    [StringLength(500, ErrorMessage = "Descrierea nu poate depăși 500 caractere")]
    public string? Descriere { get; set; }

    /// <summary>
    /// Starea activă.
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
    /// Denumirea punctului de lucru.
    /// </summary>
    public string? WorkPlaceName { get; set; }

    /// <summary>
    /// Verifică dacă locația este de tip LOC-ONLY (fără punct de lucru).
    /// </summary>
    public bool IsLocOnly => !WorkPlaceId.HasValue;

    /// <summary>
    /// Returnează adresa completă formatată sau null dacă nu există.
    /// </summary>
    public string? AdresaCompleta => string.IsNullOrEmpty(Adresa)
        ? null
        : $"{Adresa}, {Localitate}, {Judet}";

    /// <summary>
    /// Returnează descrierea tipului de locație.
    /// </summary>
    public string TipLocatieDescriere => TipLocatie switch
    {
        TipLocatie.Depozit => "Depozit",
        TipLocatie.Magazin => "Magazin",
        TipLocatie.Birou => "Birou",
        TipLocatie.Showroom => "Showroom",
        TipLocatie.Teren => "Teren",
        TipLocatie.Service => "Service",
        TipLocatie.Parcare => "Parcare",
        TipLocatie.Altele => "Altele",
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

    /// <summary>
    /// Returnează lista de capabilități active.
    /// </summary>
    public List<string> Capabilitati
    {
        get
        {
            var result = new List<string>();
            if (HasStock) result.Add("Stoc");
            if (CanSell) result.Add("Vânzare");
            if (CanPurchase) result.Add("Achiziție");
            if (CanManufacture) result.Add("Producție");
            return result;
        }
    }
}
