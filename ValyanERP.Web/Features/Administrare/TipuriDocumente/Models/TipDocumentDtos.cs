using System.ComponentModel.DataAnnotations;

namespace ValyanERP.Web.Features.Administrare.TipuriDocumente.Models;

public class CreateTipDocumentDto
{
    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string TipDocumentCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string TipDocumentName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}

public class UpdateTipDocumentDto
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [StringLength(10, MinimumLength = 1)]
    public string TipDocumentCode { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string TipDocumentName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [Required]
    public bool IsActive { get; set; }
}