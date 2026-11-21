using Erp.Core.Application.Items;
using Erp.Core.Application.Partners;

namespace Erp.Core.Tests.Sql;

/// <summary>Unirea articolelor dublate pe SQL Server real (02.01, 3.6 punctul 7).</summary>
public sealed partial class UserIntegrationTests
{
    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public async Task Duplicate_items_are_merged_with_their_barcodes()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var keep = await Send((await NewItem(Unique("DA"))) with { Name = $"Cablu Omega {tag} 2,5" });
        var remove = await Send((await NewItem(Unique("DB"))) with { Name = $"CABLU OMEGA {tag} 2.5" });
        var barcode = "2" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Send(new SaveItemBarcode(null, remove.Id, barcode, 4, null, true, null));

        Assert.Contains(await Send(new FindDuplicates(DuplicateEntity.Item)), p => p.FirstId == keep.Id && p.SecondId == remove.Id && p.Reasons == "Name");
        await Send(new MergeItems(keep.Id, remove.Id));
        Assert.Contains((await Send(new GetItem(keep.Id))).Barcodes, b => b.Barcode == barcode);
        Assert.False((await Send(new GetItem(remove.Id))).IsActive);
    }
}
