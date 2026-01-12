namespace ValyanERP.Web.Features.Administrare.Parteneri.Models;

/// <summary>
/// Cache pentru verificările ANAF (TVA, Split TVA, Inactivi, RO e-Factura).
/// </summary>
public class AnafVerificationCache
{
    /// <summary>Identificator unic.</summary>
    public Guid Id { get; set; }

    /// <summary>CUI-ul verificat.</summary>
    public string CUI { get; set; } = string.Empty;

    /// <summary>Denumirea returnată de ANAF.</summary>
    public string? Denumire { get; set; }

    /// <summary>Adresa returnată de ANAF.</summary>
    public string? Adresa { get; set; }

    /// <summary>Numărul de înregistrare Registrul Comerțului.</summary>
    public string? NrRegCom { get; set; }

    /// <summary>Codul poștal.</summary>
    public string? CodPostal { get; set; }

    /// <summary>Telefon.</summary>
    public string? Telefon { get; set; }

    /// <summary>Fax.</summary>
    public string? Fax { get; set; }

    #region Status TVA

    /// <summary>Înregistrat în scopuri TVA.</summary>
    public bool ScpTVA { get; set; }

    /// <summary>Data înregistrării în scopuri TVA.</summary>
    public DateTime? DataInregistrareTVA { get; set; }

    /// <summary>Data anulării înregistrării TVA.</summary>
    public DateTime? DataAnulareTVA { get; set; }

    /// <summary>Status TVA text.</summary>
    public string? StatusTVA { get; set; }

    /// <summary>Mesaj status TVA.</summary>
    public string? StatusTVAMessage { get; set; }

    #endregion

    #region Status Split TVA

    /// <summary>Aplică Split TVA.</summary>
    public bool StatusSplitTVA { get; set; }

    /// <summary>Data începerii Split TVA.</summary>
    public DateTime? DataInceputSplitTVA { get; set; }

    /// <summary>Data anulării Split TVA.</summary>
    public DateTime? DataAnulareSplitTVA { get; set; }

    #endregion

    #region Status Inactivi

    /// <summary>Este inactiv fiscal.</summary>
    public bool StatusInactivi { get; set; }

    /// <summary>Data inactivării.</summary>
    public DateTime? DataInactivare { get; set; }

    /// <summary>Data reactivării.</summary>
    public DateTime? DataReactivare { get; set; }

    /// <summary>Mesaj status inactivi.</summary>
    public string? StatusInactiviMessage { get; set; }

    #endregion

    #region RO e-Factura

    /// <summary>Înregistrat în RO e-Factura.</summary>
    public bool StatusRoEFactura { get; set; }

    /// <summary>Data începerii RO e-Factura.</summary>
    public DateTime? DataInceputRoEFactura { get; set; }

    #endregion

    /// <summary>Răspunsul brut JSON (pentru debugging).</summary>
    public string? RawResponse { get; set; }

    /// <summary>Data pentru care s-a făcut verificarea.</summary>
    public DateTime DataInterogare { get; set; }

    /// <summary>Timestamp verificare.</summary>
    public DateTime VerifiedAt { get; set; }

    /// <summary>Data expirării cache-ului.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Sursa verificării: ANAF, VIES, Manual.</summary>
    public string Source { get; set; } = "ANAF";

    /// <summary>ID utilizator care a creat.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>Cache-ul este valid.</summary>
    public bool IsValid => ExpiresAt > DateTime.Now;

    /// <summary>Status sumar pentru afișare.</summary>
    public string StatusSumar
    {
        get
        {
            if (StatusInactivi) return "INACTIV FISCAL";
            if (!ScpTVA) return "Neplătitor TVA";
            if (StatusSplitTVA) return "Plătitor TVA (Split)";
            return "Plătitor TVA";
        }
    }
}
