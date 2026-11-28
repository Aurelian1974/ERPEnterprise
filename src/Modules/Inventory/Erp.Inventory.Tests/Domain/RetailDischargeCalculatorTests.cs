using Erp.Inventory.Domain.Costing;

namespace Erp.Inventory.Tests.Domain;

/// <summary>Descarcarea lunara la pret cu amanuntul prin K (02.05, S3 3.7).</summary>
public sealed class RetailDischargeCalculatorTests
{
    [Fact]
    [Trait("Criteriu", "02.05-S3-A8")]
    public void November_k_for_the_cluj_store_discharges_the_markup_of_the_sales()
    {
        var k = RetailDischargeCalculator.K(41_380m, 29_610m, 251_200m, 182_400m, 21_802m, 15_829m);
        var result = RetailDischargeCalculator.Discharge(177_120m, k);
        Assert.Equal(0.179282m, result.K);
        Assert.Equal(31_754.38m, result.Markup);
        Assert.Equal(177_120m - 31_754.38m, result.Cost);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-3.7")]
    public void Sales_without_vat_come_from_the_retail_out_minus_the_vat()
    {
        var result = RetailDischargeCalculator.Calculate(new RetailDischargeInput(0, 1_210m, 605m, 0, 100m, 0, 210m, 105m));
        Assert.Equal(500m, result.SalesNet);
        Assert.Equal(0.1m, result.K);
        Assert.Equal((50m, 450m), (result.Markup, result.Cost));
    }
}
