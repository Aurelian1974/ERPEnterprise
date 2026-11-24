namespace Erp.Finance.Domain.Reporting;

/// <summary>Nivelul balantei (C5 3.2): sintetic de gradul I (3 cifre), de gradul II (4 cifre) sau analitic.</summary>
public enum BalanceLevel : byte
{
    Synthetic1 = 1,
    Synthetic2 = 2,
    Analytic = 3,
}

/// <summary>Rulajele unui cont frunza (si ale dimensiunilor cerute) cum vin din fin.usp_Balance_Get.</summary>
public sealed record BalanceInput(string? AccountCode, int? PartnerId, int? CostCenterId, int? ProjectId, decimal OpeningDebit, decimal OpeningCredit,
    decimal PriorDebit, decimal PriorCredit, decimal PeriodDebit, decimal PeriodCredit);

/// <summary>Un cont al planului: denumirea si tipul (1 activ, 2 pasiv, 3 bifunctional, null fara tip).</summary>
public sealed record BalanceAccount(string Code, string Name, byte? AccountType);

/// <summary>Un rand al balantei cu 10 coloane (C5 3.2, decizia 1).</summary>
public sealed record TrialBalanceRow(string AccountCode, string AccountName, int? PartnerId, int? CostCenterId, int? ProjectId, decimal OpeningDebit,
    decimal OpeningCredit, decimal PriorDebit, decimal PriorCredit, decimal PeriodDebit, decimal PeriodCredit, decimal TotalDebit, decimal TotalCredit,
    decimal ClosingDebit, decimal ClosingCredit);

/// <summary>O verificare a balantei (C5 regula 2): numele, rezultatul si diferenta gasita.</summary>
public sealed record BalanceCheck(string Name, bool Passed, decimal Difference);

/// <summary>
/// Balanta de verificare ca functie pura (C5 3.2): rulajele conturilor frunza se aduna pe nivelul cerut, sumele totale sunt soldurile initiale
/// + rulajele precedente + rulajele curente, iar soldul final e diferenta lor pe partea data de tipul contului (activ pe debit, pasiv pe credit,
/// bifunctional sau fara tip pe partea cu sold). Verificarile: egalitatea debit = credit pe fiecare pereche de coloane si soldul final = sumele
/// totale, pe total.
/// </summary>
public static class TrialBalance
{
    public const string UnmappedCode = "";

    /// <summary>Codul contului la nivelul cerut: analiticul, sinteticul (codul de dinaintea punctului) sau primele 3 cifre.</summary>
    public static string CodeAt(string code, BalanceLevel level)
    {
        ArgumentNullException.ThrowIfNull(code);
        var dot = code.IndexOf('.', StringComparison.Ordinal);
        var synthetic = dot < 0 ? code : code[..dot];
        return level switch
        {
            BalanceLevel.Analytic => code,
            BalanceLevel.Synthetic2 => synthetic.Length > 4 ? synthetic[..4] : synthetic,
            _ => synthetic.Length > 3 ? synthetic[..3] : synthetic,
        };
    }

    public static IReadOnlyList<TrialBalanceRow> Build(IEnumerable<BalanceInput> inputs, IReadOnlyDictionary<string, BalanceAccount> accounts, BalanceLevel level,
        bool alternativeChart, string unmappedName)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(accounts);
        return inputs
            .GroupBy(i => (Code: i.AccountCode is null ? UnmappedCode : alternativeChart ? i.AccountCode : CodeAt(i.AccountCode, level), i.PartnerId, i.CostCenterId, i.ProjectId))
            .Select(g =>
            {
                accounts.TryGetValue(g.Key.Code, out var account);
                var od = g.Sum(x => x.OpeningDebit);
                var oc = g.Sum(x => x.OpeningCredit);
                var pd = g.Sum(x => x.PriorDebit);
                var pc = g.Sum(x => x.PriorCredit);
                var cd = g.Sum(x => x.PeriodDebit);
                var cc = g.Sum(x => x.PeriodCredit);
                var (closingDebit, closingCredit) = Closing(od + pd + cd, oc + pc + cc, account?.AccountType);
                return new TrialBalanceRow(g.Key.Code, g.Key.Code == UnmappedCode ? unmappedName : account?.Name ?? g.Key.Code, g.Key.PartnerId, g.Key.CostCenterId,
                    g.Key.ProjectId, od, oc, pd, pc, cd, cc, od + pd + cd, oc + pc + cc, closingDebit, closingCredit);
            })
            .Where(r => r.TotalDebit != 0 || r.TotalCredit != 0)
            .OrderBy(r => r.AccountCode, StringComparer.Ordinal).ThenBy(r => r.PartnerId).ThenBy(r => r.CostCenterId).ThenBy(r => r.ProjectId)
            .ToList();
    }

    /// <summary>Soldul final dupa tipul contului: activ pe debit (negativ daca e creditor), pasiv pe credit, bifunctional pe partea cu sold.</summary>
    public static (decimal Debit, decimal Credit) Closing(decimal totalDebit, decimal totalCredit, byte? accountType)
    {
        var net = totalDebit - totalCredit;
        return accountType switch
        {
            1 => (net, 0m),
            2 => (0m, -net),
            _ => net >= 0 ? (net, 0m) : (0m, -net),
        };
    }

    public static IReadOnlyList<BalanceCheck> Checks(IReadOnlyList<TrialBalanceRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        BalanceCheck Pair(string name, Func<TrialBalanceRow, decimal> debit, Func<TrialBalanceRow, decimal> credit)
        {
            var difference = rows.Sum(debit) - rows.Sum(credit);
            return new BalanceCheck(name, difference == 0, difference);
        }

        var closingVsTotals = rows.Sum(r => r.ClosingDebit - r.ClosingCredit) - rows.Sum(r => r.TotalDebit - r.TotalCredit);
        return
        [
            Pair("Opening", r => r.OpeningDebit, r => r.OpeningCredit),
            Pair("Prior", r => r.PriorDebit, r => r.PriorCredit),
            Pair("Period", r => r.PeriodDebit, r => r.PeriodCredit),
            Pair("Totals", r => r.TotalDebit, r => r.TotalCredit),
            Pair("Closing", r => r.ClosingDebit, r => r.ClosingCredit),
            new BalanceCheck("ClosingEqualsTotals", closingVsTotals == 0, closingVsTotals),
        ];
    }
}
