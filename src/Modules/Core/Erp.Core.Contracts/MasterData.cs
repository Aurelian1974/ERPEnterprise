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
