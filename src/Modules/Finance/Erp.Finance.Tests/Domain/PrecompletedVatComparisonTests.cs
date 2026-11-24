using Erp.Finance.Domain.Declarations;

namespace Erp.Finance.Tests.Domain;

public sealed class PrecompletedVatComparisonTests
{
    [Fact]
    [Trait("Criteriu", "02.02-C4-A7")]
    public void Differences_are_flagged_only_over_both_thresholds()
    {
        var declared = new Dictionary<(string, string), decimal>
        {
            [("R9", "VAT")] = 50_000m, // 6.000 lei si 13,6% fata de P300: sub pragul procentual
            [("R24", "VAT")] = 30_000m, // 6.000 lei si 25%: peste ambele praguri
            [("R10", "VAT")] = 4_000m, // 4.000 lei si 100%: sub pragul valoric
            [("R13", "VAT")] = 5_000m, // fara P300: 5.000 lei, peste prag
        };
        var precompleted = new Dictionary<(string, string), decimal>
        {
            [("R9", "VAT")] = 44_000m, [("R24", "VAT")] = 24_000m, [("R10", "VAT")] = 8_000m, [("R12", "VAT")] = 1_000m,
        };

        var result = PrecompletedVatComparison.Compare(declared, precompleted).ToDictionary(r => r.RowCode);

        Assert.False(result["R9"].IsOverThreshold);
        Assert.True(result["R24"].IsOverThreshold);
        Assert.Equal(6_000m, result["R24"].Difference);
        Assert.False(result["R10"].IsOverThreshold);
        Assert.True(result["R13"].IsOverThreshold);
        Assert.False(result["R12"].IsOverThreshold);
        Assert.Equal(-1_000m, result["R12"].Difference);
    }

    [Fact]
    public void Reverse_charge_purchases_fill_a_collected_and_a_deductible_row()
    {
        var lines = D300.Compute([
            new VatSummaryRow(1, "L-S21", "Livrare", 1000m, 210m, 0m),
            new VatSummaryRow(2, "A-IC-B", "AIC", 500m, 105m, 105m),
            new VatSummaryRow(2, "A-S21", "Achizitie", 100m, 21m, 21m),
        ]).Lines;

        var rows = D300Layout.Aggregate(lines);

        Assert.Equal((1000m, 210m), rows[9]);
        Assert.Equal((500m, 105m), rows[5]);
        Assert.Equal((500m, 105m), rows[20]);
        Assert.Equal(315m, rows[D300Layout.TotalCollectedRow].Vat);
        Assert.Equal(126m, rows[D300Layout.TotalDeductibleRow].Vat);
        Assert.Equal(189m, rows[D300Layout.DueRow].Vat);
    }
}
