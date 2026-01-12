using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Models.DTOs;

/// <summary>
/// DTO pentru afișarea partenerilor în grid (date minimale).
/// </summary>
public class PartnerListDto
{
    public Guid Id { get; set; }
    public string Cod { get; set; } = string.Empty;
    public CategoriePartener Categoria { get; set; }
    public string TipEntitate { get; set; } = string.Empty;
    public RolPartener RolPartener { get; set; }
    public string PartnerStatus { get; set; } = "Activ";
    
    // Denumire
    public string DenumireAfisare { get; set; } = string.Empty;
    public string? Denumire { get; set; }
    public string? DenumireScurta { get; set; }
    public string? Nume { get; set; }
    public string? Prenume { get; set; }
    
    // Identificatori
    public string IdentificatorFiscal { get; set; } = string.Empty;
    public string? CUI { get; set; }
    public string? CIF { get; set; }
    public string? CNP { get; set; }
    public string? VATID { get; set; }
    public string? RegCom { get; set; }
    
    // Contact
    public string? Email { get; set; }
    public string? Telefon { get; set; }
    public string? Website { get; set; }
    public string? TaraOrigine { get; set; }
    
    // Status
    public bool EstePlatitorTVA { get; set; }
    public bool EsteActiv { get; set; }
    public bool EsteVerificat { get; set; }
    public string? AnafStatus { get; set; }
    public DateTime? AnafVerifiedAt { get; set; }
    public bool BlocatFacturare { get; set; }
    public bool BlocatLivrare { get; set; }
    
    // Credit
    public decimal? LimitaCredit { get; set; }
    public int? TermenPlataDef { get; set; }
    public string? CategorieComercială { get; set; }
    
    // SAF-T
    public string? CodPartenerSAFT { get; set; }
    public string? TipPartenerSAFT { get; set; }
    
    // Adresa principală
    public string? AdresaPrincipala { get; set; }
    public string? Localitate { get; set; }
    public string? Judet { get; set; }
    public string? CodPostal { get; set; }
    public string? Tara { get; set; }
    
    // Statistici
    public int NrAdrese { get; set; }
    public int NrContacte { get; set; }
    public int NrConturi { get; set; }
    public int NrReprezentanti { get; set; }
    
    // Audit
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedByUserName { get; set; }

    // Ownership
    public Guid? OwnerCompanyId { get; set; }
    public Guid? OwnerWorkPlaceId { get; set; }
    public Guid? OwnerLocationId { get; set; }
    public string? OwnerCompanyName { get; set; }
    public string? OwnerWorkPlaceName { get; set; }
    public string? OwnerLocationName { get; set; }
    
    /// <summary>Afișare entitate (Location > WorkPlace > Company).</summary>
    public string? OwnerEntityDisplay => !string.IsNullOrEmpty(OwnerLocationName) 
        ? OwnerLocationName 
        : !string.IsNullOrEmpty(OwnerWorkPlaceName)
            ? OwnerWorkPlaceName
            : OwnerCompanyName;

    // Helpers
    public bool EsteFurnizor => RolPartener.HasFlag(RolPartener.Furnizor);
    public bool EsteClient => RolPartener.HasFlag(RolPartener.Client);
    
    public string RoluriText
    {
        get
        {
            var roluri = new List<string>();
            if (RolPartener.HasFlag(RolPartener.Furnizor)) roluri.Add("Furnizor");
            if (RolPartener.HasFlag(RolPartener.Client)) roluri.Add("Client");
            if (RolPartener.HasFlag(RolPartener.Colaborator)) roluri.Add("Colaborator");
            if (RolPartener.HasFlag(RolPartener.Angajat)) roluri.Add("Angajat");
            if (RolPartener.HasFlag(RolPartener.Asociat)) roluri.Add("Asociat");
            return roluri.Count > 0 ? string.Join(", ", roluri) : "Nedefinit";
        }
    }
}
