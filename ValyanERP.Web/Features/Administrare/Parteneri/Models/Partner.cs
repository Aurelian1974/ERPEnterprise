using System.ComponentModel.DataAnnotations;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Models;

/// <summary>
/// Reprezintă un partener de afaceri (furnizor, client, PF, PJ, instituție, etc.).
/// </summary>
public class Partner
{
    /// <summary>Identificator unic.</summary>
    public Guid Id { get; set; }

    /// <summary>Cod intern unic (auto-generat: P-000001).</summary>
    [Required, StringLength(20)]
    public string Cod { get; set; } = string.Empty;

    /// <summary>Categoria principală a partenerului.</summary>
    public CategoriePartener Categoria { get; set; }

    /// <summary>Subtipul detaliat (SRL, PFA_STD, PF_RO, etc.).</summary>
    [Required, StringLength(20)]
    public string TipEntitate { get; set; } = string.Empty;

    /// <summary>Rolul în relația comercială (Flags: Furnizor, Client, etc.).</summary>
    public RolPartener RolPartener { get; set; }

    #region Identificare

    /// <summary>Denumire oficială (pentru PJ).</summary>
    [StringLength(200)]
    public string? Denumire { get; set; }

    /// <summary>Denumire scurtă/prescurtare.</summary>
    [StringLength(50)]
    public string? DenumireScurta { get; set; }

    /// <summary>Nume de familie (pentru PF).</summary>
    [StringLength(100)]
    public string? Nume { get; set; }

    /// <summary>Prenume (pentru PF).</summary>
    [StringLength(100)]
    public string? Prenume { get; set; }

    #endregion

    #region Identificatori Fiscali

    /// <summary>Cod Numeric Personal (13 cifre).</summary>
    [StringLength(13)]
    public string? CNP { get; set; }

    /// <summary>Cod Unic de Înregistrare (include prefix RO pentru plătitori TVA).</summary>
    [StringLength(20)]
    public string? CUI { get; set; }

    /// <summary>Cod Identificare Fiscală (instituții, non-profit).</summary>
    [StringLength(20)]
    public string? CIF { get; set; }

    /// <summary>VAT ID pentru entități UE.</summary>
    [StringLength(20)]
    public string? VATID { get; set; }

    /// <summary>Cod fiscal pentru entități non-UE.</summary>
    [StringLength(50)]
    public string? CodFiscalStrain { get; set; }

    /// <summary>Număr Registrul Comerțului (J../.../....).</summary>
    [StringLength(50)]
    public string? RegCom { get; set; }

    /// <summary>Număr autorizație (pentru PFA, cabinete).</summary>
    [StringLength(50)]
    public string? NrAutorizatie { get; set; }

    /// <summary>Număr pașaport (pentru PF nerezidente).</summary>
    [StringLength(50)]
    public string? Pasaport { get; set; }

    #endregion

    #region Date Specifice

    /// <summary>Data înregistrării/înființării.</summary>
    public DateTime? DataInregistrare { get; set; }

    /// <summary>Data radierii (dacă e inactiv).</summary>
    public DateTime? DataRadiere { get; set; }

    /// <summary>Codul ISO al țării de origine (RO, DE, US, etc.).</summary>
    [StringLength(3)]
    public string TaraOrigine { get; set; } = "RO";

    /// <summary>Codul CAEN principal.</summary>
    [StringLength(10)]
    public string? CAENPrincipal { get; set; }

    /// <summary>Capitalul social.</summary>
    public decimal? CapitalSocial { get; set; }

    #endregion

    #region Contact

    /// <summary>Adresa de email principală.</summary>
    [StringLength(256), EmailAddress]
    public string? Email { get; set; }

    /// <summary>Număr de telefon principal.</summary>
    [StringLength(20)]
    public string? Telefon { get; set; }

    /// <summary>Număr de telefon secundar.</summary>
    [StringLength(20)]
    public string? TelefonSecundar { get; set; }

    /// <summary>Website.</summary>
    [StringLength(200)]
    public string? Website { get; set; }

    /// <summary>URL logo.</summary>
    [StringLength(500)]
    public string? LogoUrl { get; set; }

    #endregion

    #region Status TVA

    /// <summary>Este plătitor de TVA.</summary>
    public bool EstePlatitorTVA { get; set; }

    /// <summary>Data înregistrării în scopuri TVA.</summary>
    public DateTime? DataInregistrareTVA { get; set; }

    /// <summary>Status Split TVA.</summary>
    public bool StatusSplitTVA { get; set; }

    #endregion

    #region Status Operațional

    /// <summary>Status partener: Activ, Inactiv, Blocat, Suspendat, Radiat.</summary>
    [StringLength(20)]
    public string PartnerStatus { get; set; } = "Activ";

    /// <summary>Este activ comercial.</summary>
    public bool EsteActiv { get; set; } = true;

    /// <summary>Blocat pentru facturare.</summary>
    public bool BlocatFacturare { get; set; }

    /// <summary>Blocat pentru livrare.</summary>
    public bool BlocatLivrare { get; set; }

    /// <summary>Motivul blocării.</summary>
    [StringLength(500)]
    public string? MotivBlocare { get; set; }

    #endregion

    #region Verificare ANAF

    /// <summary>A fost verificat în ANAF.</summary>
    public bool EsteVerificat { get; set; }

    /// <summary>Status returnat de ANAF.</summary>
    [StringLength(20)]
    public string? AnafStatus { get; set; }

    /// <summary>Data ultimei verificări ANAF.</summary>
    public DateTime? AnafVerifiedAt { get; set; }

    /// <summary>ID-ul utilizatorului care a verificat.</summary>
    public Guid? AnafVerifiedBy { get; set; }

    #endregion

    #region Credit și Clasificare

    /// <summary>Limita de credit acordată.</summary>
    public decimal? LimitaCredit { get; set; }

    /// <summary>Termenul de plată implicit (zile).</summary>
    public int? TermenPlataDef { get; set; } = 30;

    /// <summary>Categoria comercială: Standard, Premium, VIP.</summary>
    [StringLength(20)]
    public string? CategorieComercială { get; set; }

    #endregion

    #region SAF-T Romania

    /// <summary>Cod partener pentru SAF-T.</summary>
    [StringLength(35)]
    public string? CodPartenerSAFT { get; set; }

    /// <summary>Tip partener SAF-T: C=Client, S=Supplier, O=Other, CS=Both.</summary>
    [StringLength(10)]
    public string? TipPartenerSAFT { get; set; }

    /// <summary>ID furnizor pentru SAF-T.</summary>
    [StringLength(35)]
    public string? SupplierID { get; set; }

    /// <summary>ID client pentru SAF-T.</summary>
    [StringLength(35)]
    public string? CustomerID { get; set; }

    #endregion

    #region Identificator Temporar

    /// <summary>Identificator temporar (pentru entități fără CUI/CNP).</summary>
    [StringLength(50)]
    public string? IdentificatorTemp { get; set; }

    /// <summary>Motivul lipsei identificatorului standard.</summary>
    [StringLength(200)]
    public string? MotivLipsaIdentificator { get; set; }

    #endregion

    /// <summary>ID-ul adresei principale (denormalizat).</summary>
    public Guid? AdresaPrincipalaId { get; set; }

    /// <summary>Observații generale.</summary>
    [StringLength(2000)]
    public string? Observatii { get; set; }

    #region Audit

    /// <summary>Înregistrare activă (soft delete).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Data creării.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>ID utilizator care a creat.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>Data ultimei modificări.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>ID utilizator care a modificat.</summary>
    public Guid? UpdatedBy { get; set; }

    #endregion

    #region Navigation Properties

    /// <summary>Adresele partenerului.</summary>
    public List<PartnerAddress> Addresses { get; set; } = new();

    /// <summary>Persoanele de contact.</summary>
    public List<PartnerContact> Contacts { get; set; } = new();

    /// <summary>Conturile bancare.</summary>
    public List<PartnerBankAccount> BankAccounts { get; set; } = new();

    /// <summary>Reprezentanții legali/administrativi.</summary>
    public List<PartnerRepresentative> Representatives { get; set; } = new();

    #endregion

    #region Computed Properties

    /// <summary>Denumire pentru afișare (PJ sau PF).</summary>
    public string DenumireAfisare => Denumire ?? $"{Nume} {Prenume}".Trim();

    /// <summary>Identificatorul fiscal principal.</summary>
    public string IdentificatorFiscal => 
        CUI ?? CIF ?? CNP ?? VATID ?? CodFiscalStrain ?? IdentificatorTemp ?? string.Empty;

    /// <summary>Este furnizor.</summary>
    public bool EsteFurnizor => RolPartener.HasFlag(RolPartener.Furnizor);

    /// <summary>Este client.</summary>
    public bool EsteClient => RolPartener.HasFlag(RolPartener.Client);

    /// <summary>Este persoană fizică.</summary>
    public bool EstePersoanaFizica => Categoria == CategoriePartener.PF;

    /// <summary>Este persoană juridică.</summary>
    public bool EstePersoanaJuridica => Categoria != CategoriePartener.PF;

    #endregion
}
