using Erp.Core.Application.Items;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Sql;

/// <summary>Codurile articolelor la parteneri si traducerile pe SQL Server real (02.01, 3.6 punctele 11 si 12).</summary>
public sealed partial class UserIntegrationTests
{
    [Fact]
    [Trait("Criteriu", "02.01-3.6")]
    public async Task Item_is_found_by_the_supplier_code_and_named_in_the_partner_language()
    {
        var code = Unique("ART");
        var item = await Send(await NewItem(code));
        var supplierCode = Unique("FZ-").ToLowerInvariant();
        var saved = await Send(new SaveItemReference(null, item.Id, database.PartnerId, 2, supplierCode, "Cablu FY 2,5 (furnizor)", null, true, null));

        var duplicate = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveItemReference(null, item.Id, database.PartnerId, 2, supplierCode.ToUpperInvariant(), null, null, true, null)));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);
        var notCustomer = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveItemReference(null, item.Id, database.PartnerId, 1, "X1", null, null, true, null)));
        Assert.Equal("CORE_INVALID_REFERENCE", notCustomer.Code);

        var found = await Use<IItemCatalog, ItemReferenceDto?>(c => c.FindByPartnerCodeAsync(database.PartnerId, 2, supplierCode.ToUpperInvariant(), Ct));
        Assert.Equal(item.Id, found?.ItemId);
        Assert.Contains((await Send(new SearchItems(supplierCode))).Items, i => i.Id == item.Id);
        var list = await Send(new GetItemReferences(item.Id));
        Assert.Equal(("Cabinet Expert Audit SRL", supplierCode), (list.Single().PartnerName, list.Single().Code));

        await Send(new SaveItemReference(saved.Id, item.Id, database.PartnerId, 2, supplierCode, null, null, false, saved.RowVersion));
        Assert.Null(await Use<IItemCatalog, ItemReferenceDto?>(c => c.FindByPartnerCodeAsync(database.PartnerId, 2, supplierCode, Ct)));

        var badLanguage = await Assert.ThrowsAsync<ErpValidationException>(() =>
            Send(new SaveItemTranslations(item.Id, [new ItemTranslationDto("ro", "x")])));
        Assert.NotEmpty(badLanguage.Errors);
        await Send(new SaveItemTranslations(item.Id, [new ItemTranslationDto("EN", "FY cable 2.5 mm²"), new ItemTranslationDto("de", "Kabel FY")]));
        await Send(new SaveItemTranslations(item.Id, [new ItemTranslationDto("en", "FY cable 2.5 mm²")]));
        Assert.Equal(["en"], (await Send(new GetItemTranslations(item.Id))).Select(t => t.LanguageCode));
        Assert.Equal("FY cable 2.5 mm²", await Use<IItemCatalog, string>(c => c.GetItemNameAsync(item.Id, "en", Ct)));
        Assert.NotEqual("FY cable 2.5 mm²", await Use<IItemCatalog, string>(c => c.GetItemNameAsync(item.Id, "de", Ct)));
    }
}
