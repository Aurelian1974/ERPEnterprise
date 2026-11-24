namespace Erp.Finance.Domain.Declarations;

/// <summary>Un tip de operatiune din registrul perioadei (C4 3.4): sensul, baza, TVA-ul si partea deductibila.</summary>
public sealed record VatSummaryRow(byte Direction, string OperationTypeCode, string Name, decimal BaseRon, decimal VatRon, decimal DeductibleRon);

/// <summary>Un rand al declaratiei: codul randului, coloana, eticheta, ordinea si valoarea calculata.</summary>
public sealed record TaxLine(string RowCode, string ColumnCode, string Label, int SortOrder, decimal Value);

public sealed record TaxComputation(IReadOnlyList<TaxLine> Lines, decimal AmountDue, decimal AmountRefundable);

/// <summary>
/// Decontul de TVA (D300, C4 3.4), ca functie pura din registrul perioadei: baza si TVA-ul pe tip de operatiune, TVA colectata (livrarile si
/// taxarea inversa la achizitii), TVA deductibila (achizitiile cu drept de deducere), de plata sau de rambursat. Randurile sunt pe codurile
/// interne ale tipurilor de operatiuni; maparea pe numerele de rand ale formularului ANAF vine cu fisierul XML (C4.3).
/// </summary>
public static class D300
{
    private static readonly string[] ReverseChargePurchases = ["A-IC-B", "A-IC-S", "A-AE"];

    public static TaxComputation Compute(IReadOnlyList<VatSummaryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var lines = new List<TaxLine>();
        var order = 0;
        foreach (var r in rows.OrderBy(r => r.Direction).ThenBy(r => r.OperationTypeCode, StringComparer.Ordinal))
        {
            lines.Add(new TaxLine(r.OperationTypeCode, "BASE", r.Name, ++order, r.BaseRon));
            lines.Add(new TaxLine(r.OperationTypeCode, "VAT", r.Name, order, r.VatRon));
        }

        var collected = rows.Where(r => r.Direction == VatRegister.Sales).Sum(r => r.VatRon)
            + rows.Where(r => r.Direction == VatRegister.Purchases && ReverseChargePurchases.Contains(r.OperationTypeCode)).Sum(r => r.VatRon);
        var deductible = rows.Where(r => r.Direction == VatRegister.Purchases).Sum(r => r.DeductibleRon);
        var due = Math.Max(0, collected - deductible);
        var refundable = Math.Max(0, deductible - collected);
        lines.Add(new TaxLine("TOTAL", "COLLECTED", "TVA colectată", 1000, collected));
        lines.Add(new TaxLine("TOTAL", "DEDUCTIBLE", "TVA deductibilă", 1001, deductible));
        lines.Add(new TaxLine("TOTAL", "DUE", "TVA de plată", 1002, due));
        lines.Add(new TaxLine("TOTAL", "REFUND", "TVA de rambursat", 1003, refundable));
        return new TaxComputation(lines, due, refundable);
    }
}
