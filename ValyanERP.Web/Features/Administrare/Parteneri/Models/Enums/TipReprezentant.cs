namespace ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

/// <summary>
/// Tipul reprezentantului legal/administrativ.
/// </summary>
public enum TipReprezentant : byte
{
    /// <summary>Administrator</summary>
    Administrator = 0,
    
    /// <summary>Asociat/Acționar</summary>
    Asociat = 1,
    
    /// <summary>Împuternicit prin procură</summary>
    Imputernicit = 2,
    
    /// <summary>Reprezentant legal (pentru minori, etc.)</summary>
    ReprezentantLegal = 3
}
