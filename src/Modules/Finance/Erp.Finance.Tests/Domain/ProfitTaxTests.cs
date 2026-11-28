using Erp.Finance.Domain.Declarations;

namespace Erp.Finance.Tests.Domain;

public sealed class ProfitTaxTests
{
    [Fact]
    [Trait("Criteriu", "02.02-C4-A8")]
    public void The_quarter_three_sheet_adds_non_deductible_expenses_and_protocol_over_the_limit()
    {
        // Venituri 500.000; cheltuieli 400.000, din care amenzi 3.000 (nedeductibile) si protocol 5.000 (limita: 2% x (100.000 + 5.000) = 2.100).
        var turnover = new[]
        {
            new ProfitTaxTurnover('7', 1, -500_000m), new ProfitTaxTurnover('6', 1, 392_000m), new ProfitTaxTurnover('6', 2, 3_000m),
            new ProfitTaxTurnover('6', 3, 5_000m),
        };

        var result = ProfitTax.Compute(turnover, [new ProfitTaxManualRow("CREDITS", 1_000m, "Sponsorizare, contract 12/2026")], 1, 16m, priorTax: 10_000m);
        var rows = result.Lines.ToDictionary(l => l.RowCode, l => l.Amount);

        Assert.Equal(100_000m, rows["RESULT"]);
        Assert.Equal(3_000m, rows["NON_DEDUCTIBLE"]);
        Assert.Equal(2_900m, rows["PROTOCOL_OVER"]);
        Assert.Equal(105_900m, rows["TAXABLE"]);
        Assert.Equal(16_944m, rows["TAX"]);
        Assert.Equal(5_944m, result.TaxDue);
        Assert.Equal("Sponsorizare, contract 12/2026", result.Lines.Single(l => l.RowCode == "CREDITS").Reason);
    }

    [Fact]
    public void Micro_enterprises_pay_on_revenues_without_non_taxable_income()
    {
        var turnover = new[] { new ProfitTaxTurnover('7', 1, -300_000m), new ProfitTaxTurnover('7', 4, -20_000m), new ProfitTaxTurnover('6', 1, 250_000m) };

        var result = ProfitTax.Compute(turnover, [], 2, 1m, priorTax: 0m);

        Assert.Equal(300_000m, result.Lines.Single(l => l.RowCode == "TAXABLE").Amount);
        Assert.Equal(3_000m, result.TaxDue);
    }
}
