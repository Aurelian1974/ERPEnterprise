using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.TipuriArticole.Models;

public class ItemType
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string ItemTypeCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ItemTypeName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Ownership - în ce entitate organizațională a fost creat tipul de articol
    public Guid? OwnerCompanyId { get; set; }
    public Guid? OwnerWorkPlaceId { get; set; }
    public Guid? OwnerLocationId { get; set; }

    // View properties (populated by JOINs)
    public string? OwnerCompanyName { get; set; }
}

public class ItemTypeCreateDto
{
    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string ItemTypeCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ItemTypeName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    // Ownership
    public Guid? OwnerCompanyId { get; set; }
    public Guid? OwnerWorkPlaceId { get; set; }
    public Guid? OwnerLocationId { get; set; }
}

public class ItemTypeUpdateDto
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string ItemTypeCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string ItemTypeName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}