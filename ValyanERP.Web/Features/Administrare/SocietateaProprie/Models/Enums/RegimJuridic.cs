namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Models.Enums;

/// <summary>
/// Regimul juridic al proprietății sau spațiului.
/// </summary>
public enum RegimJuridic
{
    /// <summary>
    /// Proprietate (spațiu în proprietate).
    /// </summary>
    Proprietate = 0,

    /// <summary>
    /// Închiriere (contract de închiriere).
    /// </summary>
    Inchiriere = 1,

    /// <summary>
    /// Comodat (folosință gratuită).
    /// </summary>
    Comodat = 2,

    /// <summary>
    /// Leasing (contract de leasing).
    /// </summary>
    Leasing = 3
}
