using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.Articole.Models;

public class Articol
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string ArticolCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ArticolName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    // Mandatory association with TipArticol
    [Required]
    public Guid TipArticolId { get; set; }

    // Additional properties for inventory management
    [StringLength(20)]
    public string? UnitateMasura { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? PretAchizitie { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? PretVanzare { get; set; }

    [Range(0, int.MaxValue)]
    public int? StocMinim { get; set; }

    [Range(0, int.MaxValue)]
    public int? StocMaxim { get; set; }

    public bool IsStockable { get; set; } = true;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Ownership - în ce entitate organizațională a fost creat articolul
    public Guid? OwnerCompanyId { get; set; }
    public Guid? OwnerWorkPlaceId { get; set; }
    public Guid? OwnerLocationId { get; set; }

    // View properties (populated by JOINs)
    public string? TipArticolCode { get; set; }
    public string? TipArticolName { get; set; }
    public string? OwnerCompanyName { get; set; }
}

public class ArticolCreateDto
{
    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string ArticolCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ArticolName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    // Mandatory association with TipArticol
    [Required]
    public Guid TipArticolId { get; set; }

    // Additional properties for inventory management
    [StringLength(20)]
    public string? UnitateMasura { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? PretAchizitie { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? PretVanzare { get; set; }

    [Range(0, int.MaxValue)]
    public int? StocMinim { get; set; }

    [Range(0, int.MaxValue)]
    public int? StocMaxim { get; set; }

    public bool IsStockable { get; set; } = true;

    // Ownership
    public Guid? OwnerCompanyId { get; set; }
    public Guid? OwnerWorkPlaceId { get; set; }
    public Guid? OwnerLocationId { get; set; }
}

public class ArticolUpdateDto
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string ArticolCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ArticolName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    // Mandatory association with TipArticol
    [Required]
    public Guid TipArticolId { get; set; }

    // Additional properties for inventory management
    [StringLength(20)]
    public string? UnitateMasura { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? PretAchizitie { get; set; }

    [Range(0, double.MaxValue)]
    public decimal? PretVanzare { get; set; }

    [Range(0, int.MaxValue)]
    public int? StocMinim { get; set; }

    [Range(0, int.MaxValue)]
    public int? StocMaxim { get; set; }

    public bool IsStockable { get; set; } = true;
}