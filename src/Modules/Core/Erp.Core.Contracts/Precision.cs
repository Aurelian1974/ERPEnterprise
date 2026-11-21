namespace Erp.Core.Contracts;

/// <summary>Zecimalele unui calcul pe document: pret si cantitate (0–6, din articol sau politica firmei), valoare (2) si rotunjirea.</summary>
public sealed record PrecisionInfo(byte PriceDecimals, byte QuantityDecimals, byte AmountDecimals, MidpointRounding Rounding);

/// <summary>
/// Precizia si rotunjirea (02.01, 3.5 si 6; P7, NET-14): toate calculele de pe documente trec pe aici, interfata nu rotunjeste singura
/// (UI-05). Ierarhia: lista de preturi (cu Vanzarile), articolul, politica firmei. Documentele memoreaza valorile rotunjite; o politica
/// schimbata afecteaza doar documentele noi.
/// </summary>
public interface IPrecisionService
{
    /// <summary>Precizia pentru firma si articol; priceListId se foloseste cand exista listele de preturi (azi ignorat).</summary>
    Task<PrecisionInfo> ResolveAsync(int companyId, int? itemId, int? priceListId, CancellationToken ct);

    decimal RoundPrice(decimal value, PrecisionInfo precision);

    decimal RoundQuantity(decimal value, PrecisionInfo precision);

    /// <summary>Sumele (valoare, TVA, total) cu zecimalele valorii, 2 pentru lei.</summary>
    decimal RoundAmount(decimal value, PrecisionInfo precision);
}
