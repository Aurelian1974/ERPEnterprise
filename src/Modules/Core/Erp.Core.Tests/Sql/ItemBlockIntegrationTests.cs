using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Sql;

/// <summary>Blocarea partiala a articolului pe SQL Server real (02.01, 3.6 punctul 6).</summary>
public sealed partial class UserIntegrationTests
{
    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public async Task Item_can_be_blocked_for_purchases_with_a_reason()
    {
        var item = await Send(await NewItem(Unique("BL")));
        var saved = await Send(new SetItemBlock(item.Id, BlockFlags.Purchases, "Furnizor retras de pe piață", null));
        var block = await Send(new GetItemBlock(item.Id));
        Assert.Equal((BlockFlags.Purchases, "Furnizor retras de pe piață"), (block.BlockedFor, block.Reason));
        var tooMuch = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new SetItemBlock(item.Id, 4, "x", saved.RowVersion)));
        Assert.Contains("blockedFor", tooMuch.Errors.Keys);
        await Send(new SetItemBlock(item.Id, 0, null, saved.RowVersion));
        Assert.Equal(0, (await Send(new GetItemBlock(item.Id))).BlockedFor);
    }
}
