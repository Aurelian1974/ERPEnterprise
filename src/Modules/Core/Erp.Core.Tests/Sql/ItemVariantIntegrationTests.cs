using Erp.Core.Application.Items;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Sql;

/// <summary>Variantele articolelor pe SQL Server real (02.01, 3.6 punctul 13).</summary>
public sealed partial class UserIntegrationTests
{
    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public async Task Variants_are_generated_from_attribute_values_once_per_combination_and_take_barcodes()
    {
        var tag = Unique("").ToUpperInvariant();
        await Send(new SaveItemAttribute(null, "M" + tag, "Mărime", 10, true, null,
            [new ItemAttributeValueInput(null, "S", "S", 1, true), new ItemAttributeValueInput(null, "M", "M", 2, true), new ItemAttributeValueInput(null, "L", "L", 3, true)]));
        await Send(new SaveItemAttribute(null, "C" + tag, "Culoare", 20, true, null,
            [new ItemAttributeValueInput(null, "ROSU", "Roșu", 1, true), new ItemAttributeValueInput(null, "ALB", "Alb", 2, true)]));
        var dup = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveItemAttribute(null, "m" + tag, "Alta", 1, true, null, [])));
        Assert.Equal("CORE_DUPLICATE_CODE", dup.Code);

        var page = await Send(new GetItemAttributes());
        var size = page.Attributes.Single(a => a.Code == "M" + tag);
        var color = page.Attributes.Single(a => a.Code == "C" + tag);
        int Value(int attribute, string code) => page.Values.Single(v => v.AttributeId == attribute && v.Code == code).Id;

        var item = await Send((await NewItem(Unique("TR"))) with { Name = "Tricou Basic" });
        Assert.Equal(4, await Send(new GenerateItemVariants(item.Id, [Value(size.Id, "S"), Value(size.Id, "M"), Value(color.Id, "ROSU"), Value(color.Id, "ALB")])));
        Assert.Equal(2, await Send(new GenerateItemVariants(item.Id, [Value(size.Id, "S"), Value(size.Id, "L"), Value(color.Id, "ROSU"), Value(color.Id, "ALB")])));
        var otherSet = await Assert.ThrowsAsync<ErpException>(() => Send(new GenerateItemVariants(item.Id, [Value(size.Id, "S")])));
        Assert.Equal("CORE_INVALID_REFERENCE", otherSet.Code);

        var variants = await Send(new GetItemVariants(item.Id));
        Assert.Equal(6, variants.Variants.Count);
        Assert.Equal(new[] { size.Id, color.Id }.Order(), variants.AttributeIds.Order());
        var mRed = variants.Variants.Single(v => v.Code == "M-ROSU");
        Assert.Equal("Tricou Basic — M / Roșu", mRed.Name);

        var barcode = "2" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Send(new SaveItemBarcode(null, item.Id, barcode, 4, null, true, null, mRed.Id));
        Assert.Equal(mRed.Id, (await Send(new GetItem(item.Id))).Barcodes.Single(b => b.Barcode == barcode).VariantId);
        Assert.Equal(1, (await Send(new GetItemVariants(item.Id))).Variants.Single(v => v.Id == mRed.Id).BarcodeCount);

        var sameCode = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveItemVariant(mRed.Id, item.Id, "S-ALB", mRed.Name, true, mRed.RowVersion)));
        Assert.Equal("CORE_DUPLICATE_CODE", sameCode.Code);
        await Send(new SaveItemVariant(mRed.Id, item.Id, "M-RED", "Tricou Basic M roșu", false, mRed.RowVersion));
        Assert.False((await Send(new GetItemVariants(item.Id))).Variants.Single(v => v.Id == mRed.Id).IsActive);
    }
}
