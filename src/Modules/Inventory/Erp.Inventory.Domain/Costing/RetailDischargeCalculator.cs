namespace Erp.Inventory.Domain.Costing;

/// <summary>Datele unei luni pentru descarcare (S3 3.7): soldurile initiale si rulajele 371, 378, 4428 si vanzarile cu TVA-ul lor.</summary>
public sealed record RetailDischargeInput(decimal OpeningRetail, decimal RetailIn, decimal RetailOut, decimal OpeningMarkup, decimal MarkupIn,
    decimal OpeningVat, decimal VatIn, decimal SalesVat);

/// <summary>Rezultatul descarcarii: K (afisat la 6 zecimale), vanzarile fara TVA, adaosul descarcat si costul vanzarilor.</summary>
public sealed record RetailDischargeResult(decimal K, decimal SalesNet, decimal Markup, decimal Cost);

/// <summary>
/// Descarcarea lunara a gestiunilor la pret cu amanuntul prin coeficientul K (02.05, S3 3.7, metoda 1):
/// K = (Si 378 + Rc 378) / (Si 371 + Rd 371 − Si 4428 − Rc 4428); adaosul = vanzarile fara TVA × K (cu K nerotunjit, rotunjit la 2 zecimale);
/// costul = vanzarile fara TVA − adaosul.
/// </summary>
public static class RetailDischargeCalculator
{
    public static decimal K(decimal openingMarkup, decimal markupIn, decimal openingRetail, decimal retailIn, decimal openingVat, decimal vatIn)
    {
        var denominator = openingRetail + retailIn - openingVat - vatIn;
        return denominator == 0 ? 0 : (openingMarkup + markupIn) / denominator;
    }

    public static RetailDischargeResult Discharge(decimal salesNet, decimal k)
    {
        var markup = decimal.Round(salesNet * k, 2, MidpointRounding.AwayFromZero);
        return new RetailDischargeResult(decimal.Round(k, 6, MidpointRounding.AwayFromZero), salesNet, markup, salesNet - markup);
    }

    public static RetailDischargeResult Calculate(RetailDischargeInput input) =>
        Discharge(input.RetailOut - input.SalesVat,
            K(input.OpeningMarkup, input.MarkupIn, input.OpeningRetail, input.RetailIn, input.OpeningVat, input.VatIn));
}
