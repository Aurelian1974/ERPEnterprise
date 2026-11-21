namespace Erp.Core.Contracts;

/// <summary>Scopul pentru care se foloseste un partener sau un articol pe un document (02.01, 3.6 punctul 6).</summary>
public enum BlockPurpose : byte
{
    Sales = 1,
    Purchases = 2,
    Payments = 4,
}

public enum BlockLevel
{
    None,
    Warning,
    Blocked,
}

/// <summary>
/// Rezultatul verificarii: Blocked = documentul nu se poate valida (Code: CORE_PARTNER_BLOCKED, CORE_ITEM_BLOCKED,
/// CORE_PARTNER_INACTIVE, CORE_ITEM_INACTIVE, CORE_CREDIT_LIMIT_EXCEEDED); Warning = se afiseaza, utilizatorul confirma.
/// </summary>
public sealed record BlockCheckResult(BlockLevel Level, string? Code, string? Message)
{
    public static readonly BlockCheckResult Ok = new(BlockLevel.None, null, null);
}

/// <summary>
/// Verificarea blocarilor si a limitei de credit, apelata de module la adaugarea partenerului sau articolului pe un document si
/// la validare (02.01, 6). Blocarea partenerului e comuna tuturor firmelor; limita de credit e a firmei.
/// </summary>
public interface IMasterDataBlockCheck
{
    /// <param name="additionalAmount">Valoarea documentului curent (lei), adunata la soldul clientului pentru limita de credit.</param>
    Task<BlockCheckResult> CheckPartnerAsync(int companyId, int partnerId, BlockPurpose purpose, decimal additionalAmount, CancellationToken ct);

    Task<BlockCheckResult> CheckItemAsync(int itemId, BlockPurpose purpose, CancellationToken ct);
}

/// <summary>
/// Expunerea clientului pe firma (sold neincasat plus comenzi nefacturate), in lei; o implementeaza modulul care tine soldurile
/// (Finante / Vanzari). Pana atunci Nucleul foloseste 0, deci limita se verifica doar fata de documentul curent.
/// </summary>
public interface IPartnerExposureProvider
{
    Task<decimal> GetExposureAsync(int companyId, int partnerId, CancellationToken ct);
}

/// <summary>Codul unui articol la un client (ReferenceType 1) sau furnizor (2), cu denumirea si UM de la el (02.01, 3.6 punctul 11).</summary>
public sealed record ItemReferenceDto(int ItemId, int PartnerId, byte ReferenceType, string Code, string? Description, int? UomId);

/// <summary>
/// Catalogul articolelor pentru documente: articolul dupa codul furnizorului (receptii, import de facturi e-Factura), codul clientului
/// de tiparit pe factura (BuyersItemIdentification) si denumirea in limba documentelor partenerului (3.6 punctul 12).
/// </summary>
public interface IItemCatalog
{
    Task<ItemReferenceDto?> FindByPartnerCodeAsync(int partnerId, byte referenceType, string code, CancellationToken ct);

    Task<ItemReferenceDto?> GetPartnerCodeAsync(int itemId, int partnerId, byte referenceType, CancellationToken ct);

    /// <summary>Traducerea in limba data (ISO 639-1), altfel denumirea in romana; language null sau „ro” = romana.</summary>
    Task<string> GetItemNameAsync(int itemId, string? language, CancellationToken ct);
}
