namespace Erp.Finance.Domain.Accounts;

/// <summary>Forma unei reguli de mapare pentru verificare (C1 3.5, 4.5): sursa rezolvata la codurile conturilor romanesti si conditiile.</summary>
public sealed record MappingRuleShape(int Id, int Priority, IReadOnlySet<string> Sources, int? CompanyId, byte Side, byte? PartnerKind, int? PartnerGroupId,
    int? PartnerId, string? DimensionType, byte DimensionCondition, int? DimensionValueId, int? ItemGroupId, string? CurrencyCode, DateOnly ValidFrom,
    DateOnly? ValidTo);

/// <summary>O pereche de reguli care se pot potrivi pe aceleasi linii: castiga cea cu prioritatea mai mica; IsDead = cealalta nu se mai aplica.</summary>
public sealed record MappingOverlap(int WinnerId, int LoserId, bool IsDead, IReadOnlyList<string> CommonAccounts);

/// <summary>Regulile pure ale maparii pe planurile alternative (C1 3.5, 4.3, 4.5).</summary>
public static class MappingRules
{
    /// <summary>
    /// Codul normalizat pentru intervale (C1 3.5): simbolul sintetic, fara analitic, completat cu zerouri la 4 cifre (`628` → `6280`,
    /// `5121.01` → `5121`). Aceeasi regula ca in fin.ufn_AltMapping_FindRule.
    /// </summary>
    public static string Normalize(string code)
    {
        var synthetic = code.Split('.')[0];
        return synthetic.Length >= 4 ? synthetic : synthetic.PadRight(4, '0');
    }

    /// <summary>Contul sursa cuprinde contul si descendentii lui: sinteticele de grad inferior (prin cifre) si analiticele (prin punct).</summary>
    public static bool Covers(string sourceCode, string accountCode) =>
        string.Equals(sourceCode, accountCode, StringComparison.OrdinalIgnoreCase)
        || accountCode.StartsWith(sourceCode + ".", StringComparison.OrdinalIgnoreCase)
        || (!sourceCode.Contains('.', StringComparison.Ordinal) && accountCode.Length > sourceCode.Length
            && accountCode.StartsWith(sourceCode, StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(accountCode[sourceCode.Length]));

    /// <summary>
    /// Imparte o suma pe tinte dupa procent (C1 3.5, P7, NET-14): fiecare parte la 2 zecimale, ultima ia restul, deci suma partilor e exact
    /// suma liniei (C1-A6: 1.000,01 la 60/40 da 600,01 si 400,00).
    /// </summary>
    public static IReadOnlyList<decimal> Split(decimal amount, IReadOnlyList<decimal> percents)
    {
        ArgumentNullException.ThrowIfNull(percents);
        var parts = new decimal[percents.Count];
        var left = amount;
        for (var i = 0; i < percents.Count; i++)
        {
            parts[i] = i == percents.Count - 1 ? left : Math.Round(amount * percents[i] / 100m, 2, MidpointRounding.AwayFromZero);
            left -= parts[i];
        }

        return parts;
    }

    /// <summary>
    /// Suprapunerile dintre regulile active pe linie (fara conditie de sold), in ordinea prioritatii (C1 4.5): doua reguli se suprapun daca au
    /// conturi sursa comune, conditii compatibile si vigori care se intersecteaza; cea de mai jos „nu se aplica niciodata” daca cea de mai sus
    /// ii cuprinde toate conturile, toate conditiile si toata vigoarea.
    /// </summary>
    public static IReadOnlyList<MappingOverlap> Overlaps(IReadOnlyList<MappingRuleShape> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var ordered = rules.OrderBy(r => r.Priority).ToList();
        var result = new List<MappingOverlap>();
        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var (a, b) = (ordered[i], ordered[j]);
                var common = a.Sources.Intersect(b.Sources, StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal).ToList();
                if (common.Count == 0 || !Compatible(a, b) || !DatesIntersect(a, b))
                {
                    continue;
                }

                var dead = b.Sources.IsSubsetOf(a.Sources) && Includes(a, b) && (a.ValidFrom <= b.ValidFrom)
                    && (a.ValidTo is null || (b.ValidTo is { } bt && bt <= a.ValidTo));
                result.Add(new MappingOverlap(a.Id, b.Id, dead, common));
            }
        }

        return result;
    }

    private static bool DatesIntersect(MappingRuleShape a, MappingRuleShape b) =>
        (a.ValidTo is null || a.ValidTo >= b.ValidFrom) && (b.ValidTo is null || b.ValidTo >= a.ValidFrom);

    private static bool Same<T>(T? x, T? y) => x is null || y is null || EqualityComparer<T>.Default.Equals(x, y);

    private static bool Wider<T>(T? wide, T? narrow) => wide is null || EqualityComparer<T>.Default.Equals(wide, narrow);

    private static bool Compatible(MappingRuleShape a, MappingRuleShape b) =>
        Same(a.CompanyId, b.CompanyId) && (a.Side == 0 || b.Side == 0 || a.Side == b.Side) && Same(a.PartnerKind, b.PartnerKind)
        && Same(a.PartnerGroupId, b.PartnerGroupId) && Same(a.PartnerId, b.PartnerId) && Same(a.ItemGroupId, b.ItemGroupId)
        && Same(a.CurrencyCode, b.CurrencyCode) && DimensionsCompatible(a, b);

    private static bool DimensionsCompatible(MappingRuleShape a, MappingRuleShape b)
    {
        if (a.DimensionCondition == 0 || b.DimensionCondition == 0 || a.DimensionType != b.DimensionType)
        {
            return true;
        }

        return (a.DimensionCondition, b.DimensionCondition) switch
        {
            (1, 1) => a.DimensionValueId == b.DimensionValueId,
            (3, 3) => true,
            (3, _) or (_, 3) => false,
            _ => true,
        };
    }

    // Regula a cuprinde toate liniile regulii b: fiecare conditie a lui a e absenta sau la fel cu a lui b.
    private static bool Includes(MappingRuleShape a, MappingRuleShape b) =>
        Wider(a.CompanyId, b.CompanyId) && (a.Side == 0 || a.Side == b.Side) && Wider(a.PartnerKind, b.PartnerKind)
        && Wider(a.PartnerGroupId, b.PartnerGroupId) && Wider(a.PartnerId, b.PartnerId) && Wider(a.ItemGroupId, b.ItemGroupId)
        && Wider(a.CurrencyCode, b.CurrencyCode)
        && (a.DimensionCondition == 0
            || (a.DimensionType == b.DimensionType && (a.DimensionCondition == b.DimensionCondition && a.DimensionValueId == b.DimensionValueId
                || (a.DimensionCondition == 2 && b.DimensionCondition == 1))));
}
