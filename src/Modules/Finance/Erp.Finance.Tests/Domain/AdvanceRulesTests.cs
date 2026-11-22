using Erp.Finance.Domain.Treasury;

namespace Erp.Finance.Tests.Domain;

/// <summary>Regulile avansurilor si deconturilor (02.02, F1 3.5 si 3.7), fara baza.</summary>
public sealed class AdvanceRulesTests
{
    [Theory]
    [Trait("Criteriu", "02.02-F1-3.5")]
    [InlineData("2026-11-11", "2026-11-16")] // miercuri: joi, vineri, luni
    [InlineData("2026-11-13", "2026-11-18")] // vineri: luni, marti, miercuri
    [InlineData("2026-11-14", "2026-11-18")] // sambata
    public void The_settlement_due_date_counts_working_days(string start, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            AdvanceRules.SettlementDueDate(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), 3));

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.7")]
    public void The_per_diem_splits_the_amount_into_non_taxable_and_taxable()
    {
        static decimal Round(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(new PerDiemAmounts(150m, 150m, 0m), AdvanceRules.PerDiem(3, 50m, 57.5m, Round));
        Assert.Equal(new PerDiemAmounts(240m, 172.5m, 67.5m), AdvanceRules.PerDiem(3, 80m, 57.5m, Round));
        Assert.Equal(new PerDiemAmounts(25m, 25m, 0m), AdvanceRules.PerDiem(0.5m, 50m, 57.5m, Round));
    }
}
