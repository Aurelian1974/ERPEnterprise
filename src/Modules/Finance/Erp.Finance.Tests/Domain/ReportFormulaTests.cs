using Erp.Finance.Domain.Reporting;

namespace Erp.Finance.Tests.Domain;

public sealed class ReportFormulaTests
{
    private static readonly string[] Chart = ["121", "4423", "5121", "5121.01", "628", "701", "704", "707", "708"];

    [Fact]
    [Trait("Criteriu", "02.02-C5-A8")]
    public void Circular_references_and_missing_accounts_are_refused()
    {
        var circular = Assert.Throws<ReportFormulaException>(() => ReportFormula.Validate(
            new Dictionary<string, string> { ["A"] = "[B] + 1", ["B"] = "[C]", ["C"] = "[A] * 2" }, Chart));
        Assert.Contains("circulară", circular.Message, StringComparison.Ordinal);

        var missing = Assert.Throws<ReportFormulaException>(() => ReportFormula.Validate(new Dictionary<string, string> { ["A"] = "RC(709)" }, Chart));
        Assert.Contains("709", missing.Message, StringComparison.Ordinal);

        Assert.Throws<ReportFormulaException>(() => ReportFormula.Validate(new Dictionary<string, string> { ["A"] = "[Z]" }, Chart));
        Assert.Throws<ReportFormulaException>(() => ReportFormula.Parse("SD(5121"));
        Assert.Throws<ReportFormulaException>(() => ReportFormula.Parse("FOO(1)"));
    }

    [Fact]
    public void Rows_are_computed_from_account_measures_ranges_filters_and_other_rows()
    {
        var rows = ReportFormula.Validate(new Dictionary<string, string>
        {
            ["F20.01"] = "RC(701..708)",
            ["F20.05"] = "RD(628) + RD(628 | CC=LOG)",
            ["F20.10"] = "[F20.01] - [F20.05]",
            ["F20.11"] = "MAX(0, [F20.10]) * 16 / 100",
            ["F10.01"] = "SD(5121, 4423) - ABS(-SN(121))",
        }, Chart);

        decimal Measure(AccountNode a) => (a.Measure, a.Accounts[0].From, a.DimensionCode) switch
        {
            (AccountMeasure.TurnoverCredit, "701", _) => 1000m,
            (AccountMeasure.TurnoverDebit, "628", null) => 300m,
            (AccountMeasure.TurnoverDebit, "628", "LOG") => 100m,
            (AccountMeasure.ClosingDebit, "5121", _) => 500m,
            (AccountMeasure.ClosingNet, "121", _) => -200m,
            _ => 0m,
        };

        var values = ReportFormula.Evaluate(rows, Measure);

        Assert.Equal(600m, values["F20.10"]);
        Assert.Equal(96m, values["F20.11"]);
        Assert.Equal(300m, values["F10.01"]);
        Assert.True(new AccountRange("701", "708").Matches("704.02"));
        Assert.False(new AccountRange("701", "708").Matches("709"));
        Assert.True(new AccountRange("5121", "5121").Matches("5121.01"));
    }
}
