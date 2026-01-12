using System.ComponentModel.DataAnnotations;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Models;

/// <summary>
/// Reprezentant legal/administrativ al unui partener.
/// </summary>
public class PartnerRepresentative
{
    /// <summary>Identificator unic.</summary>
    public Guid Id { get; set; }

    /// <summary>ID-ul partenerului.</summary>
    public Guid PartnerId { get; set; }

    /// <summary>ID-ul persoanei din Persoane (dacă există în sistem).</summary>
    public Guid? PersoanaId { get; set; }

    /// <summary>Numele reprezentantului.</summary>
    [Required, StringLength(100)]
    public string Nume { get; set; } = string.Empty;

    /// <summary>Prenumele reprezentantului.</summary>
    [StringLength(100)]
    public string? Prenume { get; set; }

    /// <summary>CNP-ul reprezentantului.</summary>
    [StringLength(13)]
    public string? CNP { get; set; }

    /// <summary>Funcția (Administrator, Director, etc.).</summary>
    [Required, StringLength(100)]
    public string Functie { get; set; } = string.Empty;

    /// <summary>Tipul reprezentantului.</summary>
    public TipReprezentant TipReprezentant { get; set; } = TipReprezentant.Administrator;

    /// <summary>Are putere de semnătură.</summary>
    public bool ArePutereSemnatura { get; set; }

    /// <summary>Limita valorică pentru semnătură.</summary>
    public decimal? LimitaSemnatura { get; set; }

    /// <summary>Data numirii în funcție.</summary>
    public DateTime? DataNumire { get; set; }

    /// <summary>Data expirării mandatului.</summary>
    public DateTime? DataExpirare { get; set; }

    /// <summary>Adresa de email.</summary>
    [StringLength(256), EmailAddress]
    public string? Email { get; set; }

    /// <summary>Număr de telefon.</summary>
    [StringLength(20)]
    public string? Telefon { get; set; }

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

    /// <summary>Mandatul este activ.</summary>
    public bool MandatActiv => IsActive && (!DataExpirare.HasValue || DataExpirare.Value >= DateTime.Today);
}
