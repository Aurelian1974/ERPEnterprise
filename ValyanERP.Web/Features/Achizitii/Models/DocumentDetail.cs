using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Achizitii.Models;

public class DocumentDetail
{
    public Guid Id { get; set; }

    [Required]
    public Guid DocumentId { get; set; }

    [Required]
    public Guid ItemId { get; set; }

    // Computed properties from joins
    public string? ItemName { get; set; }
    public string? ArticolCode { get; set; }

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Quantity { get; set; }

    [Required]
    [StringLength(20)]
    public string UnitMeasure { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Ownership
    public Guid? OwnerCompanyId { get; set; }
    public Guid? OwnerWorkPlaceId { get; set; }
    public Guid? OwnerLocationId { get; set; }
}