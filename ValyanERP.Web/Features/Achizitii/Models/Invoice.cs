using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Achizitii.Models;

public class Invoice
{
    public Guid Id { get; set; }

    [Required]
    public Guid DocumentId { get; set; }

    // Navigation property
    public Document? Document { get; set; }

    [Required]
    public Guid PartnerId { get; set; }

    // Computed property from join
    public string? PartnerName { get; set; }

    // Flat properties for grid binding
    public string? DocumentNumber { get; set; }
    public DateTime? DocumentDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? DocumentStateName { get; set; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal TotalAmount { get; set; } = 0;

    [Required]
    [Range(0, double.MaxValue)]
    public decimal VATAmount { get; set; } = 0;

    [Required]
    [Range(0, double.MaxValue)]
    public decimal TotalPayment { get; set; } = 0;

    [StringLength(500)]
    public string? Observations { get; set; }

    [Required]
    public Guid UserId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    // Ownership
    public Guid? OwnerCompanyId { get; set; }
    public Guid? OwnerWorkPlaceId { get; set; }
    public Guid? OwnerLocationId { get; set; }

    // UI helper
    public int RowIndex { get; set; }
}