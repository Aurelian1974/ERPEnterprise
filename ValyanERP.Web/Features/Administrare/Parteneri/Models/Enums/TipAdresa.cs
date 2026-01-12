namespace ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;

/// <summary>
/// Tipul adresei partenerului.
/// </summary>
public enum TipAdresa : byte
{
    /// <summary>Sediu social / Domiciliu</summary>
    Sediu = 0,
    
    /// <summary>Adresă de corespondență</summary>
    Corespondenta = 1,
    
    /// <summary>Adresă de livrare</summary>
    Livrare = 2,
    
    /// <summary>Adresă de facturare</summary>
    Facturare = 3
}
