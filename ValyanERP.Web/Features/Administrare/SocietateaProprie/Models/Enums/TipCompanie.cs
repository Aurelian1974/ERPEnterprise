namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Models.Enums;

/// <summary>
/// Tipul companiei în cadrul structurii de grup.
/// </summary>
public enum TipCompanie
{
    /// <summary>
    /// Companie independentă (nu face parte dintr-un grup).
    /// </summary>
    Independent = 0,

    /// <summary>
    /// Companie holding (deține alte companii).
    /// </summary>
    Holding = 1,

    /// <summary>
    /// Companie subsidiară (deținută de un holding).
    /// </summary>
    Subsidiary = 2
}
