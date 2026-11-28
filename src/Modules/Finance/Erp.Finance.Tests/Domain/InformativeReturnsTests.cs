using Erp.Finance.Domain.Declarations;

namespace Erp.Finance.Tests.Domain;

public sealed class InformativeReturnsTests
{
    private static readonly PartnerVatRow[] Rows =
    [
        new(1, "RO111", "RO", "Client A", "L-S21", 21m, 2, 1000m, 210m),
        new(1, "RO111", "RO", "Client A", "L-S11", 11m, 1, 100m, 11m),
        new(2, null, "RO", "Persoană", "L-S21", 21m, 1, 50m, 10.5m),
        new(3, "RO333", "RO", "Furnizor", "A-AE", 21m, 1, 400m, 84m),
        new(4, "DE123", "DE", "GmbH", "A-IC-S", 21m, 1, 2000m, 420m),
        new(4, "DE123", "DE", "GmbH", "A-IC-B", 21m, 2, 3000m, 630m),
        new(5, null, "FR", "SARL", "L-IC", 0m, 1, 700m, 0m),
        new(6, "RO666", "RO", "Export", "L-EXP", 0m, 1, 900m, 0m),
    ];

    [Fact]
    public void D394_groups_domestic_operations_by_section_partner_and_rate()
    {
        var d394 = InformativeReturns.ComputeD394(Rows);

        Assert.Equal(["C", "L", "L", "L"], d394.Lines.Select(l => l.Section));
        Assert.Equal("Partener fără cod fiscal", d394.Lines.Single(l => l.PartnerId == 2).Issue);
        Assert.Equal(1150m, d394.Summary.Lines.Single(l => l is { RowCode: "L", ColumnCode: "BASE" }).Value);
        Assert.Equal(2m, d394.Summary.Lines.Single(l => l is { RowCode: "L", ColumnCode: "PARTNERS" }).Value);
    }

    [Fact]
    [Trait("Criteriu", "02.02-C4-A4")]
    public void D390_reports_intra_community_services_purchases_with_code_p()
    {
        var d390 = InformativeReturns.ComputeD390(Rows);

        Assert.Equal(("P", 2000m), (d390.Lines.Single(l => l.Section == "P").Section, d390.Lines.Single(l => l.Section == "P").BaseRon));
        Assert.Equal(3000m, d390.Lines.Single(l => l.Section == "A").BaseRon);
        Assert.Equal("Partener UE fără cod de TVA", d390.Lines.Single(l => l.Section == "L").Issue);
        Assert.DoesNotContain(d390.Lines, l => l.PartnerId == 6);
    }
}
