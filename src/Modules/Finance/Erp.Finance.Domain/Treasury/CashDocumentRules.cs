namespace Erp.Finance.Domain.Treasury;

/// <summary>Felul documentului de casa (F1 3.3).</summary>
public enum CashDocumentKind : byte
{
    Receipt = 1,
    CashIn = 2,
    CashOut = 3,
    OpeningBalance = 4,
}

/// <summary>Starea documentului de casa: ciorna, validat, stornat (doar storno, fara anulare).</summary>
public enum CashDocumentStatus : byte
{
    Draft = 1,
    Validated = 2,
    Reversed = 3,
}

/// <summary>Sensul unui tip de operatiune de trezorerie (F1 3.2); „Ambele” doar la banca (viramente interne, sume de clarificat, F2 3.2).</summary>
public enum CashDirection : byte
{
    In = 1,
    Out = 2,
    Both = 3,
}

/// <summary>Plafonul aplicat (F1 3.6): categoria si actiunea (Blocare sau Avertizare).</summary>
public enum CashLimitAction : byte
{
    Block = 1,
    Warn = 2,
}

/// <summary>O regula de plafon in vigoare la data documentului.</summary>
public sealed record CashLimit(string Category, decimal Amount, CashLimitAction Action);

/// <summary>O depasire de plafon: suma documentului in categorie, totalul zilei (inclusiv documentul) si plafonul.</summary>
public sealed record CashLimitBreach(string Category, decimal Amount, decimal DayTotal, decimal Limit, CashLimitAction Action);

/// <summary>Regulile documentelor de casa (02.02, F1 3.3 si 3.6), fara acces la date.</summary>
public static class CashDocumentRules
{
    public const string IncasareCategory = "Incasare";
    public const string PlataCategory = "Plata";
    public const string AdvanceCategory = "AvansPersoanaZi";

    /// <summary>Tipul documentului sursa al partidelor de plata din casa (F3 3.1).</summary>
    public const string SettlementSource = "Finance.CashDocument";
    public const string CashBalanceCategory = "SoldCasa";

    /// <summary>Chitanta, dispozitia de incasare si soldul initial au doar linii de intrare; dispozitia de plata doar de iesire (regula 1).</summary>
    public static CashDirection DirectionOf(CashDocumentKind kind) => kind == CashDocumentKind.CashOut ? CashDirection.Out : CashDirection.In;

    /// <summary>Tipul de document al seriei (02.01, 4.2); soldul initial nu primeste numar din serie.</summary>
    public static string? DocumentTypeOf(CashDocumentKind kind) => kind switch
    {
        CashDocumentKind.Receipt => "Finance.Receipt",
        CashDocumentKind.CashIn => "Finance.CashIn",
        CashDocumentKind.CashOut => "Finance.Payment",
        _ => null,
    };

    /// <summary>
    /// Echivalentul in lei (regulile 2 si 3, P7): totalul = rotunjire(total × curs), fiecare linie = rotunjire(suma × curs), iar diferenta
    /// de rotunjire fata de suma liniilor se pune pe linia cu valoarea absoluta cea mai mare. <paramref name="perUnit"/> = lei pe o unitate.
    /// </summary>
    public static (IReadOnlyList<decimal> Lines, decimal Total) ToRon(IReadOnlyList<decimal> amounts, decimal perUnit, Func<decimal, decimal> round)
    {
        var total = round(amounts.Sum() * perUnit);
        var lines = amounts.Select(a => round(a * perUnit)).ToArray();
        var difference = total - lines.Sum();
        if (difference != 0 && lines.Length > 0)
        {
            var largest = Enumerable.Range(0, amounts.Count).MaxBy(i => Math.Abs(amounts[i]));
            lines[largest] += difference;
        }

        return (lines, total);
    }

    /// <summary>
    /// Categoria de plafon a unei linii (regula 3): Incasare / Plata devin PJ sau PF dupa tipul partenerului; o persoana fara fisa de partener
    /// (nume + act) sau un angajat e persoana fizica. AvansPersoanaZi ramane ca atare; fara categorie = fara plafon.
    /// </summary>
    public static string? LimitCategory(string? operationCategory, bool isLegalPerson) => operationCategory switch
    {
        IncasareCategory => isLegalPerson ? "IncasarePJ" : "IncasarePF",
        PlataCategory => isLegalPerson ? "PlataPJ" : "PlataPF",
        AdvanceCategory => AdvanceCategory,
        _ => null,
    };

    /// <summary>
    /// Depasirile de plafon ale documentului (regulile 1 si 2): suma zilei pe firma, persoana si categorie, inclusiv documentul curent, se
    /// compara cu plafonul in vigoare; fara fractionare. <paramref name="documentTotals"/> si <paramref name="dayTotals"/> sunt in lei, pe categorie.
    /// </summary>
    public static IReadOnlyList<CashLimitBreach> Breaches(IReadOnlyDictionary<string, decimal> documentTotals,
        IReadOnlyDictionary<string, decimal> dayTotals, IReadOnlyList<CashLimit> limits)
    {
        var breaches = new List<CashLimitBreach>();
        foreach (var (category, amount) in documentTotals.Where(d => d.Value > 0))
        {
            var dayTotal = (dayTotals.TryGetValue(category, out var before) ? before : 0) + amount;
            breaches.AddRange(limits.Where(l => string.Equals(l.Category, category, StringComparison.Ordinal) && dayTotal > l.Amount)
                .Select(l => new CashLimitBreach(category, amount, dayTotal, l.Amount, l.Action)));
        }

        return breaches;
    }
}
