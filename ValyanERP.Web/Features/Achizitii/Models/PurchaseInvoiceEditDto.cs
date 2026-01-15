using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Achizitii.Models;

public class PurchaseInvoiceEditDto
{
    // Document ID for updates
    [Required]
    public Guid DocumentId { get; set; }

    // Document details
    [Required]
    public DateTime DocumentDate { get; set; }

    public DateTime? DueDate { get; set; }

    [Required]
    [StringLength(50)]
    public string DocumentNumber { get; set; } = string.Empty;

    [StringLength(500)]
    public string? DocumentObservations { get; set; }

    // Invoice details
    [Required]
    public Guid PartnerId { get; set; }

    [StringLength(500)]
    public string? InvoiceObservations { get; set; }

    // Line items
    [Required]
    [MinLength(1, ErrorMessage = "Factura trebuie să aibă cel puțin o linie")]
    public List<InvoiceLineItemDto> LineItems { get; set; } = new();

    // Ownership
    public Guid? OwnerCompanyId { get; set; }
    public Guid? OwnerWorkPlaceId { get; set; }
    public Guid? OwnerLocationId { get; set; }
}