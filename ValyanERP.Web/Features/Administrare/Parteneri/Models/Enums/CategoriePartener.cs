namespace ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

/// <summary>
/// Categorii principale de parteneri.
/// </summary>
public enum CategoriePartener : byte
{
    /// <summary>Persoană Fizică</summary>
    PF = 0,
    
    /// <summary>PFA / Profesii Liberale</summary>
    PFA = 1,
    
    /// <summary>Societate Comercială</summary>
    SC = 2,
    
    /// <summary>Entitate Non-Profit</summary>
    NP = 3,
    
    /// <summary>Instituție Publică RO</summary>
    IP = 4,
    
    /// <summary>Entitate Diplomatică</summary>
    DIP = 5,
    
    /// <summary>Organizație Internațională</summary>
    OI = 6,
    
    /// <summary>Entitate Străină</summary>
    STR = 7,
    
    /// <summary>Entitate Specială</summary>
    SP = 8,
    
    /// <summary>Altele</summary>
    ALT = 9
}
