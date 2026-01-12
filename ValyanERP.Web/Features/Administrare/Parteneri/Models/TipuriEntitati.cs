using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Models;

/// <summary>
/// Helper static pentru tipurile de entități și categorii.
/// </summary>
public static class TipuriEntitati
{
    /// <summary>
    /// Toate tipurile de entități organizate pe categorii.
    /// </summary>
    public static readonly Dictionary<CategoriePartener, List<TipEntitateInfo>> PerCategorie = new()
    {
        [CategoriePartener.PF] = new()
        {
            new("PF_RO", "Persoană fizică rezidentă", "CNP"),
            new("PF_NR", "Persoană fizică nerezidentă", "Pașaport/NIF"),
            new("PF_MIN", "Minor (reprezentat legal)", "CNP"),
        },
        [CategoriePartener.PFA] = new()
        {
            new("PFA_STD", "PFA - Persoană Fizică Autorizată", "CUI"),
            new("II", "Întreprindere Individuală", "CUI"),
            new("IF", "Întreprindere Familială", "CUI"),
            new("CAB_AV", "Cabinet individual avocat", "Nr. Barou"),
            new("CAB_NOT", "Cabinet notarial", "Nr. UNNPR"),
            new("CAB_MED", "Cabinet medical", "Nr. DSP"),
            new("CAB_ARH", "Cabinet arhitectură", "Nr. OAR"),
            new("CAB_EXC", "Cabinet executor", "Nr. UNEJ"),
            new("CEXP", "Expert contabil", "Nr. CECCAR"),
            new("PINS", "Practician în insolvență", "Nr. UNPIR"),
        },
        [CategoriePartener.SC] = new()
        {
            new("SRL", "Societate cu Răspundere Limitată", "CUI"),
            new("SRL_D", "SRL Debutant (Start-up Nation)", "CUI"),
            new("SA", "Societate pe Acțiuni", "CUI"),
            new("SNC", "Societate în Nume Colectiv", "CUI"),
            new("SCS", "Societate în Comandită Simplă", "CUI"),
            new("SCA", "Societate în Comandită pe Acțiuni", "CUI"),
            new("SUC", "Sucursală societate străină", "CUI"),
            new("FIL", "Filială", "CUI"),
        },
        [CategoriePartener.NP] = new()
        {
            new("ASOC", "Asociație", "CIF"),
            new("FUND", "Fundație", "CIF"),
            new("FED", "Federație", "CIF"),
            new("SIND", "Sindicat", "CIF"),
            new("PATR", "Patronat", "CIF"),
            new("PART", "Partid politic", "CIF"),
            new("CULT", "Cult religios / Unitate de cult", "CIF"),
        },
        [CategoriePartener.IP] = new()
        {
            new("IP_CEN", "Instituție publică centrală", "CIF"),
            new("IP_LOC", "Instituție publică locală", "CIF"),
            new("RA", "Regie autonomă", "CUI"),
            new("CN", "Companie națională", "CUI"),
            new("INV_PUB", "Instituție de învățământ public", "CIF"),
            new("SPIT_PUB", "Spital public", "CIF"),
            new("ANAF", "Autoritate fiscală", "CIF"),
        },
        [CategoriePartener.DIP] = new()
        {
            new("AMB", "Ambasadă", "Cod țară"),
            new("CONS", "Consulat", "Cod țară"),
            new("REP_DIP", "Reprezentanță diplomatică", "Cod intern"),
            new("MIS_DIP", "Misiune diplomatică", "Cod intern"),
        },
        [CategoriePartener.OI] = new()
        {
            new("ONU", "ONU și agenții", "Cod ONU"),
            new("UE", "Instituții Uniunea Europeană", "-"),
            new("NATO", "NATO", "-"),
            new("IFI", "Instituții financiare internaționale", "-"),
            new("OI_ALT", "Alte organizații internaționale", "-"),
        },
        [CategoriePartener.STR] = new()
        {
            new("SC_UE", "Societate comercială UE", "VAT ID"),
            new("SC_NONUE", "Societate comercială non-UE", "Cod fiscal"),
            new("IP_STR", "Instituție publică străină", "Cod național"),
            new("ONG_STR", "ONG străin", "Cod național"),
        },
        [CategoriePartener.SP] = new()
        {
            new("CONS_R", "Consorțiu", "CUI"),
            new("GIE", "Grup de interes economic", "CUI"),
            new("SE", "Societate europeană", "CUI"),
            new("SCE", "Societate cooperativă europeană", "CUI"),
            new("COOP", "Cooperativă", "CUI"),
            new("CAR", "Casa de Ajutor Reciproc", "CIF"),
            new("FOND_INV", "Fond de investiții", "Cod ASF"),
            new("FOND_PENS", "Fond de pensii", "Cod ASF"),
        },
        [CategoriePartener.ALT] = new()
        {
            new("ASOC_PROP", "Asociație de proprietari", "CIF"),
            new("HOA", "Asociație locatari", "CIF"),
            new("MOST", "Moștenire neacceptată", "Nr. dosar"),
            new("INSOLV", "Entitate în insolvență", "CUI + Nr. dosar"),
        },
    };

    /// <summary>
    /// Obține denumirea categoriei.
    /// </summary>
    public static string GetCategorieName(CategoriePartener categoria) => categoria switch
    {
        CategoriePartener.PF => "Persoană Fizică",
        CategoriePartener.PFA => "PFA / Profesii Liberale",
        CategoriePartener.SC => "Societate Comercială",
        CategoriePartener.NP => "Entitate Non-Profit",
        CategoriePartener.IP => "Instituție Publică RO",
        CategoriePartener.DIP => "Entitate Diplomatică",
        CategoriePartener.OI => "Organizație Internațională",
        CategoriePartener.STR => "Entitate Străină",
        CategoriePartener.SP => "Entitate Specială",
        CategoriePartener.ALT => "Altele",
        _ => "Necunoscut"
    };

    /// <summary>
    /// Obține informații despre un tip de entitate.
    /// </summary>
    public static TipEntitateInfo? GetTipEntitate(string cod)
    {
        foreach (var categoria in PerCategorie.Values)
        {
            var tip = categoria.FirstOrDefault(t => t.Cod == cod);
            if (tip != null) return tip;
        }
        return null;
    }

    /// <summary>
    /// Obține categoria pentru un tip de entitate.
    /// </summary>
    public static CategoriePartener? GetCategorieForTip(string cod)
    {
        foreach (var (categoria, tipuri) in PerCategorie)
        {
            if (tipuri.Any(t => t.Cod == cod)) return categoria;
        }
        return null;
    }

    /// <summary>
    /// Tipuri prioritare (cele mai frecvent utilizate).
    /// </summary>
    public static readonly string[] TipuriPrioritare = { "SRL", "SA", "PFA_STD", "PF_RO", "ASOC", "FUND" };
}

/// <summary>
/// Informații despre un tip de entitate.
/// </summary>
public record TipEntitateInfo(string Cod, string Denumire, string IdentificatorPrincipal);
