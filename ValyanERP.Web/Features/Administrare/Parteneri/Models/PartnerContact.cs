using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Models;

/// <summary>
/// Persoană de contact a unui partener.
/// </summary>
public class PartnerContact
{
    /// <summary>Identificator unic.</summary>
    public Guid Id { get; set; }

    /// <summary>ID-ul partenerului.</summary>
    public Guid PartnerId { get; set; }

    /// <summary>Numele persoanei de contact.</summary>
    [Required, StringLength(100)]
    public string Nume { get; set; } = string.Empty;

    /// <summary>Prenumele persoanei de contact.</summary>
    [StringLength(100)]
    public string? Prenume { get; set; }

    /// <summary>Funcția în cadrul partenerului.</summary>
    [StringLength(100)]
    public string? Functie { get; set; }

    /// <summary>Departamentul.</summary>
    [StringLength(100)]
    public string? Departament { get; set; }

    /// <summary>Adresa de email.</summary>
    [StringLength(256), EmailAddress]
    public string? Email { get; set; }

    /// <summary>Număr de telefon fix.</summary>
    [StringLength(20)]
    public string? Telefon { get; set; }

    /// <summary>Număr de telefon mobil.</summary>
    [StringLength(20)]
    public string? TelefonMobil { get; set; }

    /// <summary>Este persoană cu putere de decizie.</summary>
    public bool EsteDecident { get; set; }

    /// <summary>Este contactul principal.</summary>
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

    /// <summary>Numele complet pentru afișare.</summary>
    public string NumeComplet => string.IsNullOrEmpty(Prenume) ? Nume : $"{Nume} {Prenume}";
}
