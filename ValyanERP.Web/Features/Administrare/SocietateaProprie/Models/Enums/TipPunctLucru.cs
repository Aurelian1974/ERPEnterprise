namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Models.Enums;

/// <summary>
/// Tipul punctului de lucru conform ONRC.
/// </summary>
public enum TipPunctLucru
{
    /// <summary>
    /// Sediu social (adresa oficială a companiei).
    /// </summary>
    SediuSocial = 0,

    /// <summary>
    /// Sucursală (unitate cu autonomie limitată).
    /// </summary>
    Sucursala = 1,

    /// <summary>
    /// Agenție (punct de reprezentare).
    /// </summary>
    Agentie = 2,

    /// <summary>
    /// Punct de lucru simplu.
    /// </summary>
    PunctLucru = 3
}
