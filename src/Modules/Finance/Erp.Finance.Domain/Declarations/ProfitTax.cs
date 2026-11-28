namespace Erp.Finance.Domain.Declarations;

/// <summary>Rulajul net (debit − credit) al conturilor unei clase (6, 7) pe tratamentul fiscal (1 deductibil, 2 nedeductibil, 3 limitat, 4 neimpozabil).</summary>
public sealed record ProfitTaxTurnover(char AccountClass, byte TaxTreatment, decimal Net);

/// <summary>Un rand manual al fisei, cu motiv (decizia 6).</summary>
public sealed record ProfitTaxManualRow(string RowCode, decimal Amount, string Reason);

public sealed record ProfitTaxLine(string RowCode, string Label, int SortOrder, bool IsManual, decimal Amount, string? Reason);

public sealed record ProfitTaxResult(IReadOnlyList<ProfitTaxLine> Lines, decimal TaxDue);

/// <summary>
/// Fisa de calcul a impozitului pe profit (C4 3.6), cumulat de la inceputul anului, ca functie pura: veniturile si cheltuielile (fara 691, 698),
/// rezultatul contabil, cheltuielile nedeductibile (tratamentul 2) si partea de protocol peste limita (tratamentul 3: 2% din rezultatul contabil
/// la care se adauga cheltuielile de protocol), veniturile neimpozabile (4), deducerile si pierderea de recuperat (manuale), profitul impozabil,
/// impozitul la cota, creditele fiscale (manuale) si impozitul declarat in trimestrele anterioare. Pentru microintreprinderi: veniturile fara
/// cele neimpozabile, la cota microintreprinderii.
/// </summary>
public static class ProfitTax
{
    public const decimal DefaultProfitRate = 16m;
    public const decimal DefaultMicroRate = 1m;
    public const decimal ProtocolLimitPercent = 2m;

    public static readonly string[] ManualRows = ["DEDUCTIONS", "LOSS", "CREDITS"];

    public static ProfitTaxResult Compute(IReadOnlyList<ProfitTaxTurnover> turnover, IReadOnlyList<ProfitTaxManualRow> manual, byte regime, decimal rate,
        decimal priorTax)
    {
        ArgumentNullException.ThrowIfNull(turnover);
        ArgumentNullException.ThrowIfNull(manual);
        decimal Net(char cls, params byte[] treatments) => turnover.Where(t => t.AccountClass == cls && (treatments.Length == 0 || treatments.Contains(t.TaxTreatment)))
            .Sum(t => t.Net);
        decimal Manual(string row) => manual.Where(m => m.RowCode == row).Sum(m => m.Amount);
        string? Reason(string row) => manual.FirstOrDefault(m => m.RowCode == row)?.Reason;

        var revenues = -Net('7');
        var lines = new List<ProfitTaxLine>();
        var order = 0;
        void Add(string code, string label, decimal amount, bool isManual = false) =>
            lines.Add(new ProfitTaxLine(code, label, ++order, isManual, decimal.Round(amount, 2), isManual ? Reason(code) : null));

        if (regime == 2)
        {
            var nonTaxableMicro = -Net('7', 4);
            Add("REVENUES", "Venituri totale", revenues);
            Add("NON_TAXABLE", "Venituri neimpozabile", nonTaxableMicro);
            var microBase = Math.Max(0, revenues - nonTaxableMicro);
            Add("TAXABLE", "Baza impozabilă", microBase);
            var microTax = decimal.Round(microBase * rate / 100m, 0, MidpointRounding.AwayFromZero);
            Add("TAX", $"Impozit pe venit ({rate:0.##}%)", microTax);
            Add("PRIOR", "Impozit declarat în trimestrele anterioare", priorTax);
            var microDue = Math.Max(0, microTax - priorTax);
            Add("DUE", "Impozitul trimestrului", microDue);
            return new ProfitTaxResult(lines, microDue);
        }

        var expenses = Net('6');
        var result = revenues - expenses;
        var nonDeductible = Net('6', 2);
        var protocol = Net('6', 3);
        var protocolLimit = Math.Max(0, (result + protocol) * ProtocolLimitPercent / 100m);
        var protocolOver = Math.Max(0, protocol - protocolLimit);
        var nonTaxable = -Net('7', 4);
        Add("REVENUES", "Venituri totale", revenues);
        Add("EXPENSES", "Cheltuieli totale (fără impozitul pe profit)", expenses);
        Add("RESULT", "Rezultatul contabil", result);
        Add("NON_DEDUCTIBLE", "Cheltuieli nedeductibile", nonDeductible);
        Add("PROTOCOL_OVER", "Cheltuieli de protocol peste limita de 2%", protocolOver);
        Add("NON_TAXABLE", "Venituri neimpozabile", nonTaxable);
        Add("DEDUCTIONS", "Deduceri și elemente similare", Manual("DEDUCTIONS"), true);
        var beforeLoss = result + nonDeductible + protocolOver - nonTaxable - Manual("DEDUCTIONS");
        Add("LOSS", "Pierderea fiscală de recuperat", Manual("LOSS"), true);
        var taxable = Math.Max(0, beforeLoss - Manual("LOSS"));
        Add("TAXABLE", "Profitul impozabil", taxable);
        var tax = decimal.Round(taxable * rate / 100m, 0, MidpointRounding.AwayFromZero);
        Add("TAX", $"Impozit pe profit ({rate:0.##}%)", tax);
        Add("CREDITS", "Credite fiscale (sponsorizări, impozit plătit în străinătate)", Manual("CREDITS"), true);
        Add("PRIOR", "Impozit declarat în trimestrele anterioare", priorTax);
        var due = Math.Max(0, tax - Manual("CREDITS") - priorTax);
        Add("DUE", "Impozitul trimestrului", due);
        return new ProfitTaxResult(lines, due);
    }
}
