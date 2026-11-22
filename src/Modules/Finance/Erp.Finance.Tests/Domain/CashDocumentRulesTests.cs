using Erp.Finance.Domain.Treasury;

namespace Erp.Finance.Tests.Domain;

/// <summary>Regulile documentelor de casa (02.02, F1 3.3 si 3.6), fara baza.</summary>
public sealed class CashDocumentRulesTests
{
    private static decimal Round2(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    [Fact]
    [Trait("Criteriu", "02.02-F1-A15")]
    public void The_ron_equivalent_rounds_the_document_total_and_puts_the_difference_on_the_largest_line()
    {
        // 3 linii in euro la cursul 5,0812: totalul in lei = round(30,03 × 5,0812) = 152,59; liniile rotunjite separat dau 152,58.
        var (lines, total) = CashDocumentRules.ToRon([10.01m, 10.01m, 10.01m], 5.0812m, Round2);

        Assert.Equal(152.59m, total);
        Assert.Equal(total, lines.Sum());
        Assert.Equal([50.87m, 50.86m, 50.86m], lines);
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-A15")]
    public void Ron_documents_keep_their_amounts()
    {
        var (lines, total) = CashDocumentRules.ToRon([100m, 0.5m], 1m, Round2);
        Assert.Equal(100.5m, total);
        Assert.Equal([100m, 0.5m], lines);
    }

    [Theory]
    [Trait("Criteriu", "02.02-F1-3.6")]
    [InlineData("Incasare", true, "IncasarePJ")]
    [InlineData("Incasare", false, "IncasarePF")]
    [InlineData("Plata", true, "PlataPJ")]
    [InlineData("Plata", false, "PlataPF")]
    [InlineData("AvansPersoanaZi", false, "AvansPersoanaZi")]
    [InlineData(null, true, null)]
    public void The_limit_category_follows_the_person_type(string? operationCategory, bool legal, string? expected) =>
        Assert.Equal(expected, CashDocumentRules.LimitCategory(operationCategory, legal));

    [Fact]
    [Trait("Criteriu", "02.02-F1-A4")]
    public void The_day_total_includes_the_current_document_so_splitting_does_not_avoid_the_limit()
    {
        var limits = new[] { new CashLimit("IncasarePJ", 5000m, CashLimitAction.Block), new CashLimit("IncasarePJ", 2000m, CashLimitAction.Block) };
        var breaches = CashDocumentRules.Breaches(new Dictionary<string, decimal> { ["IncasarePJ"] = 3000m },
            new Dictionary<string, decimal> { ["IncasarePJ"] = 3000m }, limits);

        Assert.Equal([(6000m, 5000m), (6000m, 2000m)], breaches.Select(b => (b.DayTotal, b.Limit)));
        Assert.Empty(CashDocumentRules.Breaches(new Dictionary<string, decimal> { ["IncasarePJ"] = 1999m }, new Dictionary<string, decimal>(), limits));
    }

    [Fact]
    [Trait("Criteriu", "02.02-F1-3.3")]
    public void Receipts_and_cash_in_take_in_lines_and_payments_take_out_lines()
    {
        Assert.Equal(CashDirection.In, CashDocumentRules.DirectionOf(CashDocumentKind.Receipt));
        Assert.Equal(CashDirection.Out, CashDocumentRules.DirectionOf(CashDocumentKind.CashOut));
        Assert.Equal("Finance.CashIn", CashDocumentRules.DocumentTypeOf(CashDocumentKind.CashIn));
        Assert.Null(CashDocumentRules.DocumentTypeOf(CashDocumentKind.OpeningBalance));
    }
}
