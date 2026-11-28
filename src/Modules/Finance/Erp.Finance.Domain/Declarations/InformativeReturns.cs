namespace Erp.Finance.Domain.Declarations;

/// <summary>Registrul perioadei pe partener, tip de operatiune si cota (C4 4.2).</summary>
public sealed record PartnerVatRow(int? PartnerId, string? PartnerFiscalCode, string? PartnerCountryCode, string? PartnerName, string OperationTypeCode,
    decimal VatRatePercent, int Documents, decimal BaseRon, decimal VatRon);

/// <summary>O linie pe partener a unei declaratii informative: sectiunea (D394) sau codul operatiunii (D390) si problema gasita, daca e.</summary>
public sealed record PartnerLine(string Section, int? PartnerId, string? PartnerFiscalCode, string? PartnerCountryCode, string? PartnerName, decimal VatRatePercent,
    int Documents, decimal BaseRon, decimal VatRon, string? Issue);

public sealed record InformativeComputation(TaxComputation Summary, IReadOnlyList<PartnerLine> Lines);

/// <summary>
/// Declaratiile informative (C4 4.2, regula 2: tipul de operatiune decide sectiunea), ca functii pure din registrul pe partener: D394 pentru
/// operatiunile din tara (L livrari, V livrari cu taxare inversa, A achizitii, C achizitii cu taxare inversa), D390 pentru cele intracomunitare
/// (L livrari de bunuri, S prestari de servicii, A achizitii de bunuri, P achizitii de servicii). Partenerii fara cod fiscal (D394) sau fara
/// cod de TVA UE (D390) se semnaleaza pe linie; verificarea la ANAF si VIES vine cu Integrari.
/// </summary>
public static class InformativeReturns
{
    private static readonly Dictionary<string, string> D394Sections = new(StringComparer.Ordinal)
    {
        ["L-S21"] = "L", ["L-S19"] = "L", ["L-S11"] = "L", ["L-S9"] = "L", ["L-S5"] = "L", ["L-AE"] = "V",
        ["A-S21"] = "A", ["A-S19"] = "A", ["A-S11"] = "A", ["A-S9"] = "A", ["A-S5"] = "A", ["A-NOD"] = "A", ["A-AE"] = "C",
    };

    private static readonly Dictionary<string, string> D390Codes = new(StringComparer.Ordinal)
    {
        ["L-IC"] = "L", ["L-O"] = "S", ["A-IC-B"] = "A", ["A-IC-S"] = "P",
    };

    public static string? D394Section(string operationTypeCode) => D394Sections.GetValueOrDefault(operationTypeCode);

    public static string? D390Code(string operationTypeCode) => D390Codes.GetValueOrDefault(operationTypeCode);

    public static InformativeComputation ComputeD394(IReadOnlyList<PartnerVatRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var lines = rows.Where(r => D394Section(r.OperationTypeCode) is not null && (r.PartnerCountryCode ?? "RO") == "RO")
            .GroupBy(r => (Section: D394Section(r.OperationTypeCode)!, r.PartnerId, r.PartnerFiscalCode, r.VatRatePercent))
            .Select(g => Line(g.Key.Section, g, g.Key.VatRatePercent,
                string.IsNullOrWhiteSpace(g.Key.PartnerFiscalCode) ? "Partener fără cod fiscal" : null))
            .OrderBy(l => l.Section, StringComparer.Ordinal).ThenBy(l => l.PartnerFiscalCode, StringComparer.Ordinal).ThenBy(l => l.VatRatePercent).ToList();
        return new InformativeComputation(Summary(lines), lines);
    }

    public static InformativeComputation ComputeD390(IReadOnlyList<PartnerVatRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var lines = rows.Where(r => D390Code(r.OperationTypeCode) is not null)
            .GroupBy(r => (Section: D390Code(r.OperationTypeCode)!, r.PartnerId, r.PartnerFiscalCode, r.PartnerCountryCode))
            .Select(g => Line(g.Key.Section, g, 0, string.IsNullOrWhiteSpace(g.Key.PartnerFiscalCode) ? "Partener UE fără cod de TVA"
                : g.Key.PartnerCountryCode is null or "RO" ? "Partenerul nu e dintr-un alt stat membru" : null))
            .OrderBy(l => l.Section, StringComparer.Ordinal).ThenBy(l => l.PartnerCountryCode, StringComparer.Ordinal)
            .ThenBy(l => l.PartnerFiscalCode, StringComparer.Ordinal).ToList();
        return new InformativeComputation(Summary(lines), lines);
    }

    private static PartnerLine Line(string section, IEnumerable<PartnerVatRow> group, decimal rate, string? issue)
    {
        var rows = group.ToList();
        var first = rows[0];
        return new PartnerLine(section, first.PartnerId, first.PartnerFiscalCode, first.PartnerCountryCode, rows.Select(r => r.PartnerName).FirstOrDefault(n => n is not null),
            rate, rows.Sum(r => r.Documents), rows.Sum(r => r.BaseRon), rows.Sum(r => r.VatRon), issue);
    }

    /// <summary>Totalurile pe sectiune (baza, TVA, numarul de parteneri), randurile calculate ale declaratiei.</summary>
    private static TaxComputation Summary(IReadOnlyList<PartnerLine> lines)
    {
        var result = new List<TaxLine>();
        var order = 0;
        foreach (var g in lines.GroupBy(l => l.Section).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            order++;
            result.Add(new TaxLine(g.Key, "BASE", $"Secțiunea {g.Key}", order, g.Sum(l => l.BaseRon)));
            result.Add(new TaxLine(g.Key, "VAT", $"Secțiunea {g.Key}", order, g.Sum(l => l.VatRon)));
            result.Add(new TaxLine(g.Key, "PARTNERS", $"Secțiunea {g.Key}", order, g.Select(l => (l.PartnerId, l.PartnerFiscalCode)).Distinct().Count()));
        }

        return new TaxComputation(result, 0, 0);
    }
}
