using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Achizitii.Models;

public class Document
{
    public Guid Id { get; set; }

    [Required]
    public DateTime DocumentDate { get; set; }

    public DateTime? DueDate { get; set; }

    [Required]
    [StringLength(50)]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(10)]
    public string DocumentTypeCode { get; set; } = "FFA"; // Factura Furnizor Achizitie

    [Required]
    public Guid DocumentStateId { get; set; }

    // Navigation property
    public DocumentState? DocumentState { get; set; }

    // Computed property from join
    public string? DocumentStateName { get; set; }

    [StringLength(500)]
    public string? Observations { get; set; }

    [Required]
    public Guid UserId { get; set; }

    public Guid? EntityIntroduced { get; set; }

    [Required]
    public DateTime IntroductionDate { get; set; }

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