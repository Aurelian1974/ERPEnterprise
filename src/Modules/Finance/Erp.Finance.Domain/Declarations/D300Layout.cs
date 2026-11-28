namespace Erp.Finance.Domain.Declarations;

/// <summary>
/// Randurile formularului D300 (versiunea livrata, C4 3.4) pe tipurile de operatiuni ale registrului: livrarile pe randurile colectate, achizitiile
/// pe cele deductibile, iar achizitiile cu taxare inversa si pe un rand colectat. Numerele de rand se verifica la fiecare versiune noua a
/// formularului ANAF (RAMAS-DE-FACUT, verificari externe); XML-ul scrie baza pe atributul R{rand}_1 si TVA-ul pe R{rand}_2.
/// </summary>
public static class D300Layout
{
    public const int TotalCollectedRow = 17;
    public const int TotalDeductibleRow = 31;
    public const int DueRow = 40;
    public const int RefundRow = 41;

    private static readonly Dictionary<string, int[]> Rows = new(StringComparer.Ordinal)
    {
        ["L-IC"] = [1], ["L-O"] = [3], ["L-S21"] = [9], ["L-S19"] = [9], ["L-S11"] = [10], ["L-S9"] = [10], ["L-S5"] = [11], ["L-AE"] = [13],
        ["L-EXP"] = [14], ["L-Z"] = [14], ["L-E"] = [15],
        ["A-IC-B"] = [5, 20], ["A-IC-S"] = [7, 21], ["A-AE"] = [12, 27], ["A-IMP"] = [22], ["A-S21"] = [24], ["A-S19"] = [24], ["A-S11"] = [25],
        ["A-S9"] = [25], ["A-S5"] = [26], ["A-NOD"] = [30],
    };

    /// <summary>Randurile formularului pentru un tip de operatiune; un tip necunoscut nu are rand (si se semnaleaza la verificare).</summary>
    public static IReadOnlyList<int> RowsOf(string operationTypeCode) => Rows.TryGetValue(operationTypeCode, out var r) ? r : [];

    /// <summary>Valorile pe randurile formularului (baza, TVA), adunate din randurile calculate ale declaratiei si din totaluri.</summary>
    public static IReadOnlyDictionary<int, (decimal Base, decimal Vat)> Aggregate(IEnumerable<TaxLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new SortedDictionary<int, (decimal Base, decimal Vat)>();
        foreach (var line in lines)
        {
            if (line.RowCode == "TOTAL")
            {
                var row = line.ColumnCode switch { "COLLECTED" => TotalCollectedRow, "DEDUCTIBLE" => TotalDeductibleRow, "DUE" => DueRow, _ => RefundRow };
                result[row] = (0, line.Value);
                continue;
            }

            foreach (var row in RowsOf(line.RowCode))
            {
                var current = result.GetValueOrDefault(row);
                result[row] = line.ColumnCode == "BASE" ? (current.Base + line.Value, current.Vat) : (current.Base, current.Vat + line.Value);
            }
        }

        return result;
    }
}

/// <summary>Un rand al comparatiei cu decontul precompletat P300 (RO e-TVA, C4 3.4).</summary>
public sealed record PrecompletedDifference(string RowCode, string ColumnCode, decimal Declared, decimal Precompleted, decimal Difference, bool IsOverThreshold);

/// <summary>
/// Comparatia D300 – P300 (RO e-TVA, decizia 5): pe fiecare rand, diferenta e peste prag cand e de cel putin 5.000 lei si de cel putin 20% din
/// valoarea precompletata (pragurile din 2026, livrate ca parametri). Nu blocheaza generarea; randurile peste prag cer explicatie.
/// </summary>
public static class PrecompletedVatComparison
{
    public const decimal MinimumAmount = 5000m;
    public const decimal MinimumPercent = 20m;

    public static IReadOnlyList<PrecompletedDifference> Compare(IReadOnlyDictionary<(string Row, string Column), decimal> declared,
        IReadOnlyDictionary<(string Row, string Column), decimal> precompleted, decimal minimumAmount = MinimumAmount, decimal minimumPercent = MinimumPercent)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(precompleted);
        return declared.Keys.Union(precompleted.Keys).OrderBy(k => k.Row, StringComparer.Ordinal).ThenBy(k => k.Column, StringComparer.Ordinal).Select(k =>
        {
            var d = declared.GetValueOrDefault(k);
            var p = precompleted.GetValueOrDefault(k);
            var diff = d - p;
            var over = Math.Abs(diff) >= minimumAmount && Math.Abs(diff) * 100m >= minimumPercent * Math.Abs(p);
            return new PrecompletedDifference(k.Row, k.Column, d, p, diff, over);
        }).ToList();
    }

    /// <summary>Valorile D300 pe randurile formularului, in cheile comparatiei (R{rand}, BASE/VAT).</summary>
    public static IReadOnlyDictionary<(string Row, string Column), decimal> Declared(IEnumerable<TaxLine> lines) =>
        D300Layout.Aggregate(lines).SelectMany(r => new[]
        {
            ((Row: $"R{r.Key}", Column: "BASE"), r.Value.Base), ((Row: $"R{r.Key}", Column: "VAT"), r.Value.Vat),
        }).Where(x => x.Item2 != 0).ToDictionary(x => x.Item1, x => x.Item2);
}
