using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Achizitii.Models;

public class InvoiceDetail
{
    public Guid Id { get; set; }

    [Required]
    public Guid InvoiceId { get; set; }

    [Required]
    public Guid DocumentDetailId { get; set; }

    // Navigation properties
    public DocumentDetail? DocumentDetail { get; set; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal UnitPrice { get; set; }

    [Required]
    [Range(0, 100)]
    public decimal VATRate { get; set; } = 0;

    [Required]
    [Range(0, double.MaxValue)]
    public decimal VATAmount { get; set; } = 0;

    [Required]
    [Range(0, double.MaxValue)]
    public decimal LineTotal { get; set; }

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