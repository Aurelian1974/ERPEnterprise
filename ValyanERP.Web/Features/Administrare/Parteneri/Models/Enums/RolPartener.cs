namespace ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

/// <summary>
/// Rolul partenerului în relația comercială (Flags pentru roluri multiple).
/// </summary>
[Flags]
public enum RolPartener
{
    /// <summary>Nedefinit</summary>
    None = 0,
    
    /// <summary>Furnizor de bunuri/servicii</summary>
    Furnizor = 1,
    
    /// <summary>Client (cumpărător)</summary>
    Client = 2,
    
    /// <summary>Colaborator extern</summary>
    Colaborator = 4,
    
    /// <summary>Angajat (pentru PF)</summary>
    Angajat = 8,
    
    /// <summary>Asociat/Acționar</summary>
    Asociat = 16
}
