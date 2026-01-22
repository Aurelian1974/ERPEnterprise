using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Achizitii.Models;

public class PurchaseInvoiceCreateDto
{
    // Document details
    [Required]
    public DateTime DocumentDate { get; set; }

    public DateTime? DueDate { get; set; }

    [Required]
    [StringLength(50)]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string DocumentStateCode { get; set; } = "C"; // Default to Draft

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

    // Selected partner addresses by type (nullable)
    public Guid? PartnerAddressSediuId { get; set; }
    public Guid? PartnerAddressCorespondentaId { get; set; }
    public Guid? PartnerAddressLivrareId { get; set; }
    public Guid? PartnerAddressFacturareId { get; set; }

    // Selected partner contact and bank account
    public Guid? PartnerContactId { get; set; }
    public Guid? PartnerBankAccountId { get; set; }
}

public class InvoiceLineItemDto
{
    [Required]
    public Guid ItemId { get; set; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Cantitatea trebuie să fie mai mare decât 0")]
    public decimal Quantity { get; set; }

    [Required]
    [StringLength(20)]
    public string UnitMeasure { get; set; } = string.Empty;

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Prețul unitar nu poate fi negativ")]
    public decimal UnitPrice { get; set; }

    [Required]
    [Range(0, 100, ErrorMessage = "Cota TVA trebuie să fie între 0 și 100")]
    public decimal VATRate { get; set; } = 0;
}