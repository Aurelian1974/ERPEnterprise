using Erp.Finance.Domain.Reporting;

namespace Erp.Finance.Tests.Domain;

/// <summary>Balanta de verificare (02.02, C5 3.2, regula 2): nivelurile, soldurile finale dupa tipul contului si verificarile egalitatilor.</summary>
public sealed class TrialBalanceTests
{
    private static readonly Dictionary<string, BalanceAccount> Accounts = new[]
    {
        new BalanceAccount("401", "Furnizori", 2), new BalanceAccount("401.01", "Furnizori interni", 2), new BalanceAccount("5121", "Conturi la bănci în lei", 1),
        new BalanceAccount("512", "Conturi curente la bănci", 1), new BalanceAccount("4423", "TVA de plată", 2), new BalanceAccount("442", "Taxa pe valoarea adăugată", 3),
        new BalanceAccount("4424", "TVA de recuperat", 1), new BalanceAccount("628", "Alte cheltuieli", 1),
    }.ToDictionary(a => a.Code);

    private static BalanceInput In(string code, decimal od = 0, decimal oc = 0, decimal pd = 0, decimal pc = 0, decimal cd = 0, decimal cc = 0) =>
        new(code, null, null, null, od, oc, pd, pc, cd, cc);

    [Fact]
    [Trait("Criteriu", "02.02-C5-A1")]
    public void Columns_add_up_and_the_synthetic_is_the_sum_of_its_analytics()
    {
        var inputs = new[] { In("5121", od: 10_000, pd: 2_000, pc: 500, cd: 1_000, cc: 4_000), In("401.01", oc: 10_000, pd: 500, pc: 2_000, cd: 4_000, cc: 1_000),
            In("4423", cd: 300), In("4424", cc: 300) };
        var analytic = TrialBalance.Build(inputs, Accounts, BalanceLevel.Analytic, false, "Nemapat");
        Assert.All(TrialBalance.Checks(analytic), c => Assert.True(c.Passed, c.Name));
        var bank = analytic.Single(r => r.AccountCode == "5121");
        Assert.Equal((13_000m, 4_500m, 8_500m, 0m), (bank.TotalDebit, bank.TotalCredit, bank.ClosingDebit, bank.ClosingCredit));
        var suppliers = analytic.Single(r => r.AccountCode == "401.01");
        Assert.Equal((0m, 8_500m), (suppliers.ClosingDebit, suppliers.ClosingCredit));

        var grade1 = TrialBalance.Build(inputs, Accounts, BalanceLevel.Synthetic1, false, "Nemapat");
        Assert.Equal(["401", "442", "512"], grade1.Select(r => r.AccountCode));
        Assert.Equal(analytic.Where(r => r.AccountCode.StartsWith("44", StringComparison.Ordinal)).Sum(r => r.PeriodDebit), grade1.Single(r => r.AccountCode == "442").PeriodDebit);
        Assert.Equal("Taxa pe valoarea adăugată", grade1.Single(r => r.AccountCode == "442").AccountName);
        Assert.All(TrialBalance.Checks(grade1), c => Assert.True(c.Passed, c.Name));
        Assert.Equal(["401", "4423", "4424", "5121"], TrialBalance.Build(inputs, Accounts, BalanceLevel.Synthetic2, false, "Nemapat").Select(r => r.AccountCode));
    }

    [Fact]
    public void Closing_balances_follow_the_account_type()
    {
        Assert.Equal((-100m, 0m), TrialBalance.Closing(100, 200, 1));
        Assert.Equal((0m, 100m), TrialBalance.Closing(100, 200, 2));
        Assert.Equal((0m, 100m), TrialBalance.Closing(100, 200, 3));
        Assert.Equal((50m, 0m), TrialBalance.Closing(150, 100, null));
        Assert.Equal("4111", TrialBalance.CodeAt("4111.01", BalanceLevel.Synthetic2));
        Assert.Equal("411", TrialBalance.CodeAt("4111.01", BalanceLevel.Synthetic1));
        Assert.Equal("628", TrialBalance.CodeAt("628.10", BalanceLevel.Synthetic2));
    }

    [Fact]
    public void An_unbalanced_input_fails_the_checks_and_unmapped_lines_keep_their_own_row()
    {
        var rows = TrialBalance.Build([In("628", cd: 100), new BalanceInput(null, null, null, null, 0, 0, 0, 0, 0, 90)], Accounts, BalanceLevel.Analytic, true, "Nemapat");
        Assert.Contains(rows, r => r.AccountCode == TrialBalance.UnmappedCode && r.AccountName == "Nemapat");
        Assert.Contains(TrialBalance.Checks(rows), c => c is { Name: "Period", Passed: false, Difference: 10 });
    }
}
