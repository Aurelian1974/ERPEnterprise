using System.Globalization;
using Erp.Finance.Contracts.Accounting;

namespace Erp.Finance.Domain.Declarations;

/// <summary>O cota TVA din nomenclatorul Nucleului: codul, procentul, categoria UBL (S, Z, E, AE, K, G, O) si vigoarea.</summary>
public sealed record VatRateInfo(string Code, decimal Percent, string Category, DateOnly ValidFrom, DateOnly? ValidTo);

/// <summary>Un rand al registrului de TVA (C4 3.1), inainte de datele partenerului si de perioada.</summary>
public sealed record VatRow(string OperationTypeCode, string VatRateCode, decimal VatRatePercent, decimal BaseRon, decimal VatRon);

/// <summary>
/// Registrul de TVA al unui document (C4 3.1, 3.2), ca functie pura: componentele Net si Vat ale contractului de contare (C2 3.1) se grupeaza pe
/// cota; tipul de operatiune vine din sensul documentului, categoria cotei, tipul partenerului (1 RO, 2 UE, 3 non-UE, 4 persoana fizica),
/// articolul (bunuri sau servicii) si marcaje (ReverseCharge, Services). Documentele fara cota pe componente (facturile pe totaluri) primesc
/// cota standard cea mai apropiata de raportul TVA / baza la data documentului; fara TVA, regimul dupa partener.
/// </summary>
public static class VatRegister
{
    public const byte Sales = 1;
    public const byte Purchases = 2;

    public static IReadOnlyList<VatRow> Build(byte direction, IReadOnlyList<string> flags, byte? partnerKind, IReadOnlyList<AccountingComponent> components,
        DateOnly date, IReadOnlyList<VatRateInfo> rates)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(rates);
        var relevant = components.Where(c => c.Component is "Net" or "Vat").ToList();
        if (relevant.Count == 0)
        {
            return [];
        }

        var services = flags.Contains("Services", StringComparer.OrdinalIgnoreCase)
            || relevant.Any(c => string.Equals(c.ItemType, "Service", StringComparison.OrdinalIgnoreCase));
        var reverse = flags.Contains("ReverseCharge", StringComparer.OrdinalIgnoreCase);
        return relevant
            .GroupBy(c => c.VatRateCode)
            .Select(g =>
            {
                var net = g.Where(c => c.Component == "Net").Sum(c => c.AmountRon);
                var vat = g.Where(c => c.Component == "Vat").Sum(c => c.AmountRon);
                var rate = g.Key is { } code ? Find(rates, code, date) : Derive(direction, partnerKind, reverse, net, vat, date, rates);
                return new VatRow(OperationType(direction, rate, partnerKind, reverse, services), rate.Code, rate.Percent, net, vat);
            })
            .Where(r => r.BaseRon != 0 || r.VatRon != 0)
            .ToList();
    }

    /// <summary>Tipul de operatiune (C4 3.2) pentru sens, cota, partener si marcaje.</summary>
    public static string OperationType(byte direction, VatRateInfo rate, byte? partnerKind, bool reverseCharge, bool services)
    {
        ArgumentNullException.ThrowIfNull(rate);
        var percent = ((int)rate.Percent).ToString(CultureInfo.InvariantCulture);
        if (direction == Sales)
        {
            return rate.Category switch
            {
                "K" => "L-IC",
                "G" => "L-EXP",
                "AE" => "L-AE",
                "E" => "L-E",
                "Z" => "L-Z",
                "O" => "L-O",
                _ when reverseCharge => "L-AE",
                _ => $"L-S{percent}",
            };
        }

        return partnerKind switch
        {
            2 => services ? "A-IC-S" : "A-IC-B",
            3 => "A-IMP",
            _ when reverseCharge || rate.Category == "AE" => "A-AE",
            _ when rate.Category == "E" => "A-NOD",
            _ => $"A-S{percent}",
        };
    }

    private static VatRateInfo Find(IReadOnlyList<VatRateInfo> rates, string code, DateOnly date) =>
        rates.Where(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase) && r.ValidFrom <= date && (r.ValidTo is null || r.ValidTo >= date))
            .OrderByDescending(r => r.ValidFrom).FirstOrDefault()
        ?? rates.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? new VatRateInfo(code, 0m, "S", DateOnly.MinValue, null);

    private static VatRateInfo Derive(byte direction, byte? partnerKind, bool reverse, decimal net, decimal vat, DateOnly date, IReadOnlyList<VatRateInfo> rates)
    {
        var valid = rates.Where(r => r.ValidFrom <= date && (r.ValidTo is null || r.ValidTo >= date)).ToList();
        if (vat == 0)
        {
            var category = (direction, partnerKind) switch
            {
                (Sales, 2) => "K",
                (Sales, 3) => "G",
                _ when reverse || partnerKind == 2 => "AE",
                _ => "E",
            };
            return valid.FirstOrDefault(r => r.Category == category) ?? new VatRateInfo(category, 0m, category, DateOnly.MinValue, null);
        }

        var percent = net == 0 ? 0m : Math.Abs(vat / net) * 100m;
        return valid.Where(r => r.Category == "S" && r.Percent > 0).OrderBy(r => Math.Abs(r.Percent - percent)).FirstOrDefault()
            ?? new VatRateInfo("S", Math.Round(percent), "S", DateOnly.MinValue, null);
    }
}
