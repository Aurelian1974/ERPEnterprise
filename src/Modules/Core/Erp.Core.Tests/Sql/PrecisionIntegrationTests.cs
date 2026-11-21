using Erp.Core.Application.Precision;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Sql;

/// <summary>Politicile de zecimale pe SQL Server real (02.01, 3.5; macheta 07d): salvare, protectia politicii folosite, ierarhia.</summary>
public sealed partial class UserIntegrationTests
{
    [Fact]
    [Trait("Criteriu", "02.01-3.5")]
    public async Task Precision_comes_from_the_item_then_the_company_policy_and_a_used_policy_stays_active()
    {
        var code = Unique("EN").ToUpperInvariant();
        var saved = await Send(new SavePrecisionPolicy(null, code, "Energie: preț cu 6 zecimale", 6, 3, 1, true, null));
        var duplicate = await Assert.ThrowsAsync<ErpException>(() => Send(new SavePrecisionPolicy(null, code.ToLowerInvariant(), "Dublură", 2, 2, 1, true, null)));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);

        var page = await Send(new GetPrecisionPolicies());
        var standard = page.Policies.Single(p => p.Code == "STANDARD");
        Assert.True(standard.CompanyCount >= 1);
        Assert.Contains(page.Policies, p => p.Id == saved.Id && p.AmountPrecision == 2);
        var inUse = await Assert.ThrowsAsync<ErpException>(() => Send(new SavePrecisionPolicy(standard.Id, standard.Code, standard.Name,
            standard.PricePrecision, standard.QuantityPrecision, standard.RoundingMode, false, standard.RowVersion)));
        Assert.Equal("CORE_ENTITY_IN_USE", inUse.Code);

        var company = await Send(new ResolvePrecision());
        Assert.Equal((standard.PricePrecision, standard.QuantityPrecision, (byte)2, MidpointRounding.AwayFromZero),
            (company.PriceDecimals, company.QuantityDecimals, company.AmountDecimals, company.Rounding));
        var item = await Send((await NewItem(Unique("PR"))) with { PricePrecision = 6 });
        var withItem = await Send(new ResolvePrecision(item.Id));
        Assert.Equal(((byte)6, standard.QuantityPrecision), (withItem.PriceDecimals, withItem.QuantityDecimals));
    }
}
