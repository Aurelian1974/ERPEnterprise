using System.ComponentModel.DataAnnotations;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Models.DTOs;

/// <summary>
/// DTO pentru actualizarea unui partener.
/// </summary>
public class UpdatePartnerDto
{
    [Required]
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Categoria este obligatorie")]
    public CategoriePartener Categoria { get; set; }

    [Required(ErrorMessage = "Tipul entității este obligatoriu")]
    [StringLength(20)]
    public string TipEntitate { get; set; } = string.Empty;

    public RolPartener RolPartener { get; set; } = RolPartener.None;

    // Identificare PJ
    [StringLength(200)]
    public string? Denumire { get; set; }

    [StringLength(50)]
    public string? DenumireScurta { get; set; }

    // Identificare PF
    [StringLength(100)]
    public string? Nume { get; set; }

    [StringLength(100)]
    public string? Prenume { get; set; }

    // Identificatori fiscali
    [StringLength(13)]
    public string? CNP { get; set; }

    [StringLength(20)]
    public string? CUI { get; set; }

    [StringLength(20)]
    public string? CIF { get; set; }

    [StringLength(20)]
    public string? VATID { get; set; }

    [StringLength(50)]
    public string? CodFiscalStrain { get; set; }

    [StringLength(50)]
    public string? RegCom { get; set; }

    [StringLength(50)]
    public string? NrAutorizatie { get; set; }

    [StringLength(50)]
    public string? Pasaport { get; set; }

    // Date specifice
    public DateTime? DataInregistrare { get; set; }
    public DateTime? DataRadiere { get; set; }

    [StringLength(3)]
    public string TaraOrigine { get; set; } = "RO";

    [StringLength(10)]
    public string? CAENPrincipal { get; set; }

    public decimal? CapitalSocial { get; set; }

    // Contact
    [StringLength(256), EmailAddress(ErrorMessage = "Email invalid")]
    public string? Email { get; set; }

    [StringLength(20)]
    public string? Telefon { get; set; }

    [StringLength(20)]
    public string? TelefonSecundar { get; set; }

    [StringLength(200)]
    public string? Website { get; set; }

    // TVA
    public bool EstePlatitorTVA { get; set; }
    public DateTime? DataInregistrareTVA { get; set; }
    public bool StatusSplitTVA { get; set; }

    // Status
    [StringLength(20)]
    public string PartnerStatus { get; set; } = "Activ";
    
    public bool EsteActiv { get; set; } = true;
    public bool BlocatFacturare { get; set; }
    public bool BlocatLivrare { get; set; }

    [StringLength(500)]
    public string? MotivBlocare { get; set; }

    // Credit
    public decimal? LimitaCredit { get; set; }
    public int? TermenPlataDef { get; set; } = 30;

    [StringLength(20)]
    public string? CategorieComercială { get; set; }

    // Temporar
    [StringLength(50)]
    public string? IdentificatorTemp { get; set; }

    [StringLength(200)]
    public string? MotivLipsaIdentificator { get; set; }

    // Note
    [StringLength(2000)]
    public string? Observatii { get; set; }
}
