using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.Persoane.Models;

public class Persoana
{
    public Guid Id { get; set; }
    
    [Required]
    [StringLength(100)]
    public string Nume { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Prenume { get; set; } = string.Empty;

    public string NumeComplet => $"{Nume} {Prenume}";

    [StringLength(13)]
    public string? CNP { get; set; }

    public DateTime? DataNasterii { get; set; }

    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    [Phone]
    [StringLength(20)]
    public string? Telefon { get; set; }

    [StringLength(500)]
    public string? Adresa { get; set; }

    [StringLength(100)]
    public string? Oras { get; set; }

    [StringLength(100)]
    public string? Judet { get; set; }

    [StringLength(10)]
    public string? CodPostal { get; set; }

    [StringLength(100)]
    public string? Tara { get; set; } = "Romania";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    
    // Ownership - în ce entitate organizațională a fost creată înregistrarea
    public Guid? OwnerCompanyId { get; set; }
    public Guid? OwnerWorkPlaceId { get; set; }
    public Guid? OwnerLocationId { get; set; }
    
    // View properties (populated by JOINs)
    public string? OwnerCompanyName { get; set; }
}
