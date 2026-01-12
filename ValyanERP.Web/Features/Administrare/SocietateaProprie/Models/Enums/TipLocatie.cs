namespace ValyanERP.Web.Features.Administrare.SocietateaProprie.Models.Enums;

/// <summary>
/// Tipul locației în cadrul structurii organizaționale.
/// </summary>
public enum TipLocatie
{
    /// <summary>
    /// Depozit pentru stocuri.
    /// </summary>
    Depozit = 0,

    /// <summary>
    /// Magazin de vânzare.
    /// </summary>
    Magazin = 1,

    /// <summary>
    /// Birou administrativ.
    /// </summary>
    Birou = 2,

    /// <summary>
    /// Showroom pentru expunere produse.
    /// </summary>
    Showroom = 3,

    /// <summary>
    /// Teren (proprietate funciară).
    /// </summary>
    Teren = 4,

    /// <summary>
    /// Service sau atelier.
    /// </summary>
    Service = 5,

    /// <summary>
    /// Parcare.
    /// </summary>
    Parcare = 6,

    /// <summary>
    /// Alt tip de locație.
    /// </summary>
    Altele = 7
}
