using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Achizitii.Models;

public class DocumentState
{
    public Guid Id { get; set; }

    [Required]
    [StringLength(10)]
    public string CodStare { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    public string DenumireStare { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Descriere { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}