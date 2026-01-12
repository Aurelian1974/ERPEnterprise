using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Models;

/// <summary>
/// Reprezintă un grup de companii (holding).
/// </summary>
public class CompanyGroup
{
    /// <summary>
    /// Identificator unic.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Denumirea grupului de companii.
    /// </summary>
    [Required(ErrorMessage = "Denumirea este obligatorie")]
    [StringLength(200, ErrorMessage = "Denumirea nu poate depăși 200 caractere")]
    public string Denumire { get; set; } = string.Empty;

    /// <summary>
    /// Descrierea grupului.
    /// </summary>
    [StringLength(500, ErrorMessage = "Descrierea nu poate depăși 500 caractere")]
    public string? Descriere { get; set; }

    /// <summary>
    /// URL-ul logo-ului grupului.
    /// </summary>
    [StringLength(500)]
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Website-ul grupului.
    /// </summary>
    [StringLength(200)]
    [Url(ErrorMessage = "Website-ul nu este valid")]
    public string? Website { get; set; }

    /// <summary>
    /// Starea activă a grupului.
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
    /// Numărul de companii din grup.
    /// </summary>
    public int CompanyCount { get; set; }
}
