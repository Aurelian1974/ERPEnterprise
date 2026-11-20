using Erp.Core.Application.Items;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Tests.Sql;

/// <summary>Articolele si nomenclatoarele lor (02.01, 3.3 si 3.5) pe SQL Server real, in baza testelor de parteneri.</summary>
public sealed partial class PartnerIntegrationTests
{
    private async Task<ItemLookupsDto> ItemLookups() => await Send(new GetItemLookups());

    private static string ItemCode() => "I" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<SaveItem> NewItem(string? code, string name, byte type = 1, bool stockable = true)
    {
        var lookups = await ItemLookups();
        var set = lookups.UomSets.Single(s => s.Code == "BUC");
        var vat = lookups.VatRates.Single(v => v.Code == "S21");
        return new SaveItem(null, code, name, type, set.Id, vat.Id, null, null, null, stockable, null, null, null, null, null, true, null);
    }

    /// <summary>Un EAN-13 valid (cifra de control GS1), diferit la fiecare apel.</summary>
    private static string NewEan13()
    {
        var body = "594" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var control = 0; control <= 9; control++)
        {
            if (Erp.Core.Domain.Barcodes.Gs1CheckDigit(body + control))
            {
                return body + control;
            }
        }

        throw new InvalidOperationException();
    }

    [Fact]
    public async Task Shipped_vat_rates_units_and_uom_sets_follow_the_current_law_and_unece_codes()
    {
        var lookups = await ItemLookups();
        var current = lookups.VatRates.Where(v => v.ValidTo is null && v.Category == "S").Select(v => (v.Code, v.Percent, v.ValidFrom)).ToList();
        Assert.Contains(("S21", "21.00", new DateOnly(2025, 8, 1)), current);
        Assert.Contains(("R11", "11.00", new DateOnly(2025, 8, 1)), current);
        Assert.Equal(new DateOnly(2025, 7, 31), lookups.VatRates.Single(v => v.Code == "S19").ValidTo);
        Assert.Equal("H87", lookups.Units.Single(u => u.Code == "buc").UneceCode);

        var drinks = lookups.UomSets.Single(s => s.Code == "BAUT05");
        Assert.Equal(["1", "6", "24", "480"], drinks.Lines.Select(l => l.FactorToBase));
        Assert.Single(drinks.Lines, l => l.IsBase);
    }

    [Fact]
    public async Task Vat_rates_are_added_with_a_validity_and_a_used_rate_can_only_be_closed()
    {
        var code = "T" + Random.Shared.Next(100, 999);
        await Send(new SaveVatRates([new VatRateInput(null, code, "Test", "24.00", "S", new DateOnly(2027, 1, 1), null, null)]));
        var lookups = await ItemLookups();
        var rate = lookups.VatRates.Single(v => v.Code == code);

        var overlap = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveVatRates([new VatRateInput(null, code, "Suprapusă", "25.00", "S", new DateOnly(2027, 6, 1), null, null)])));
        Assert.Equal("CORE_INVALID_REFERENCE", overlap.Code);

        var zeroCategory = await Assert.ThrowsAsync<ErpValidationException>(() =>
            Send(new SaveVatRates([new VatRateInput(null, code + "Z", "Scutit", "5.00", "E", new DateOnly(2027, 1, 1), null, null)])));
        Assert.Contains(zeroCategory.Errors.Keys, k => k.EndsWith("percent", StringComparison.Ordinal));

        // Folosita de un articol: procentul nu se mai schimba, dar cota se poate inchide.
        var item = await NewItem(ItemCode(), "Cu cota test");
        await Send(item with { DefaultVatRateId = rate.Id });
        rate = (await ItemLookups()).VatRates.Single(v => v.Id == rate.Id);
        var changed = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveVatRates([new VatRateInput(rate.Id, rate.Code, rate.Name, "23.00", "S",
            rate.ValidFrom, null, rate.RowVersion)])));
        Assert.Equal("CORE_ENTITY_IN_USE", changed.Code);
        await Send(new SaveVatRates([new VatRateInput(rate.Id, rate.Code, rate.Name, rate.Percent, "S", rate.ValidFrom, new DateOnly(2027, 12, 31), rate.RowVersion)]));
        Assert.Equal(new DateOnly(2027, 12, 31), (await ItemLookups()).VatRates.Single(v => v.Id == rate.Id).ValidTo);
    }

    [Fact]
    public async Task Units_have_unique_unece_codes_and_sets_have_one_base_that_items_follow()
    {
        var unece = "Z" + (char)('A' + Random.Shared.Next(26)) + (char)('A' + Random.Shared.Next(26));
        await Send(new SaveUnitsOfMeasure([new UnitOfMeasureInput(null, "u" + unece, "Unitate test", unece, true, null)]));
        var duplicate = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveUnitsOfMeasure([new UnitOfMeasureInput(null, "x" + unece, "Altă unitate", unece, true, null)])));
        Assert.Equal("CORE_DUPLICATE_CODE", duplicate.Code);

        var lookups = await ItemLookups();
        var piece = lookups.Units.Single(u => u.UneceCode == "H87").Id;
        var box = lookups.Units.Single(u => u.UneceCode == "XBX").Id;
        var twoBases = await Assert.ThrowsAsync<ErpValidationException>(() => Send(new SaveUomSet(null, "S" + unece, "Două baze",
            true, [new UomLineDto(piece, "1", true, false, false, false), new UomLineDto(box, "1", true, false, false, false)], null)));
        Assert.Contains("lines", twoBases.Errors.Keys);

        var set = await Send(new SaveUomSet(null, "S" + unece, "Bax 12", true,
            [new UomLineDto(piece, "1", true, false, false, true), new UomLineDto(box, "12", false, true, true, false)], null));
        var item = await Send((await NewItem(ItemCode(), "În set nou")) with { UomSetId = set.Id });
        Assert.Equal(piece, (await Send(new GetItem(item.Id))).BaseUomId);

        // Baza setului devine baxul: articolele din set o urmeaza.
        var saved = (await ItemLookups()).UomSets.Single(s => s.Id == set.Id);
        await Send(new SaveUomSet(set.Id, saved.Code, saved.Name, true, [new UomLineDto(box, "1", true, true, true, true)], saved.RowVersion));
        Assert.Equal(box, (await Send(new GetItem(item.Id))).BaseUomId);
    }

    [Fact]
    public async Task Item_groups_form_a_tree_without_cycles_and_search_includes_subgroups()
    {
        var root = await Send(new SaveItemGroup(null, ItemCode(), "Băuturi", null, 10, true, null));
        var child = await Send(new SaveItemGroup(null, ItemCode(), "Ape minerale", root.Id, 10, true, null));
        var groups = (await ItemLookups()).Groups;
        var rootGroup = groups.Single(g => g.Id == root.Id);
        var cycle = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveItemGroup(root.Id, rootGroup.Code, rootGroup.Name, child.Id, 10, true, rootGroup.RowVersion)));
        Assert.Equal("CORE_INVALID_REFERENCE", cycle.Code);

        var prefix = "Z" + Guid.NewGuid().ToString("N")[..6];
        await Send((await NewItem(ItemCode(), prefix + " Apă minerală 0,5 L")) with { ItemGroupId = child.Id });
        var found = await Send(new SearchItems(ItemGroupId: root.Id));
        Assert.Contains(found.Items, i => i.Name.StartsWith(prefix, StringComparison.Ordinal));

        var inUse = await Assert.ThrowsAsync<ErpException>(() =>
            Send(new SaveItemGroup(child.Id, groups.Single(g => g.Id == child.Id).Code, "Ape minerale", root.Id, 10, false, groups.Single(g => g.Id == child.Id).RowVersion)));
        Assert.Equal("CORE_ENTITY_IN_USE", inUse.Code);
    }

    [Fact]
    public async Task Items_get_codes_validate_type_rules_and_are_found_by_name_code_or_barcode()
    {
        var prefix = "Y" + Guid.NewGuid().ToString("N")[..6];
        var first = await Send(await NewItem(null, prefix + " Sirop fructe"));
        var detail = await Send(new GetItem(first.Id));
        Assert.StartsWith("A-", detail.Code, StringComparison.Ordinal);

        var duplicate = await Assert.ThrowsAsync<ErpException>(async () => await Send(await NewItem(detail.Code.ToLowerInvariant(), "Altul")));
        Assert.Equal("CORE_ITEM_DUPLICATE_CODE", duplicate.Code);
        var stockableService = await Assert.ThrowsAsync<ErpValidationException>(async () => await Send(await NewItem(ItemCode(), "Transport", type: 6)));
        Assert.Contains("isStockable", stockableService.Errors.Keys);
        var notPackaging = await Assert.ThrowsAsync<ErpException>(async () => await Send((await NewItem(ItemCode(), "Cu SGR greșit")) with { DepositItemId = first.Id }));
        Assert.Equal("CORE_INVALID_REFERENCE", notPackaging.Code);

        // Garantia SGR e un ambalaj; exceptia de UM: bax de 12 doar pentru acest articol.
        var deposit = await Send(await NewItem(ItemCode(), prefix + " Garanție SGR PET", type: 7));
        var lookups = await ItemLookups();
        var box = lookups.Units.Single(u => u.UneceCode == "XBX").Id;
        var water = await Send((await NewItem(ItemCode(), prefix + " Apă plată")) with
        {
            DepositItemId = deposit.Id, CnCode = "2201 10 11", NetWeightKg = "0.5", GrossWeightKg = "0.523",
            Uoms = [new UomLineDto(box, "12", false, true, false, false)],
        });
        var waterDetail = await Send(new GetItem(water.Id));
        Assert.Equal(("22011011", "0.523", "12"), (waterDetail.CnCode, waterDetail.GrossWeightKg, Assert.Single(waterDetail.Uoms).FactorToBase));

        var ean = NewEan13();
        var invalid = await Assert.ThrowsAsync<ErpValidationException>(() =>
            Send(new SaveItemBarcode(null, water.Id, ean[..12] + ((ean[12] - '0' + 1) % 10), 1, null, true, null)));
        Assert.Contains("barcode", invalid.Errors.Keys);
        await Send(new SaveItemBarcode(null, water.Id, ean, 1, box, true, null));
        var duplicateBarcode = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveItemBarcode(null, first.Id, ean, 1, null, true, null)));
        Assert.Equal("CORE_ITEM_DUPLICATE_BARCODE", duplicateBarcode.Code);

        Assert.Equal([water.Id], (await Send(new SearchItems(ean))).Items.Select(i => i.Id));
        Assert.Equal(3, (await Send(new SearchItems(prefix.ToLowerInvariant()))).Total);
        Assert.Equal([deposit.Id], (await Send(new SearchItems(prefix, ItemType: 7))).Items.Select(i => i.Id));

        var history = await Send(new GetItemHistory(water.Id));
        Assert.Equal(["Core.ItemBarcode", "Core.Item"], history.Select(h => h.EntityType));
    }

    private static IReadOnlyList<string?> ItemRow(string? code, string name, string type = "Marfă", string set = "BUC", string vat = "S21",
        string? barcode = null, string? deposit = null, string? stockable = null) =>
        [code, name, type, stockable, set, vat, null, barcode, null, deposit, null, null, "0,5", null, null, null];

    private async Task<ImportJobDto> UploadItems(IEnumerable<IReadOnlyList<string?>> rows)
    {
        var content = Convert.ToBase64String(Sheet.Write(ItemSheet.Definition, [.. rows], []));
        var analysis = await Send(new AnalyzeItemImport("articole.xlsx", content));
        return await Send(new CreateItemImportJob(analysis));
    }

    private async Task<ImportJobDto> ConfirmItems(int jobId)
    {
        while (!(await Send(new ImportItemBatch(jobId))).Done)
        {
        }

        return await Send(new GetImportJob(jobId));
    }

    [Fact]
    public async Task Item_export_uses_the_import_columns_and_imports_update_by_code_with_barcodes()
    {
        var template = Sheet.Read(ItemSheet.Definition, (await Send(new GetItemImportTemplate())).Content);
        Assert.Equal("BAUT05", Assert.Single(template)["UomSet"]);

        var prefix = "W" + Guid.NewGuid().ToString("N")[..6];
        var existing = await Send(await NewItem(prefix + "-EX", prefix + " Existent"));
        var taken = NewEan13();
        await Send(new SaveItemBarcode(null, existing.Id, taken, 1, null, true, null));
        var ean = NewEan13();
        IReadOnlyList<string?>[] rows =
        [
            ItemRow(null, prefix + " Nou din serie", barcode: ean),                 // 2: valid, cod din serie, cod de bare nou
            ItemRow(prefix + "-EX", prefix + " Existent actualizat", set: "KG"),    // 3: actualizare dupa cod
            ItemRow(prefix + "-3", prefix + " Set greșit", set: "NUEXISTA"),        // 4: set necunoscut
            ItemRow(prefix + "-4", prefix + " EAN greșit", barcode: "5941234500012"), // 5: cifra de control
            ItemRow(prefix + "-5", prefix + " EAN luat", barcode: taken),           // 6: cod de bare al altui articol
            ItemRow(prefix + "-6", prefix + " Serviciu stocabil", type: "Serviciu", stockable: "Da"), // 7: validatorul fisei
            ItemRow(prefix + "-7", prefix + " Cu SGR", deposit: "NU-EXISTA"),       // 8: garantie SGR necunoscuta
        ];
        var job = await UploadItems(rows);
        Assert.Equal([4, 5, 6, 7, 8], job.Errors.Select(e => e.Row).Distinct());
        Assert.Contains(job.Warnings, w => w.Row == 3);

        var done = await ConfirmItems(job.Id);
        Assert.Equal((ImportStatus.Imported, 2), (done.Status, done.ImportedRows));
        var lookups = await ItemLookups();
        var updated = await Send(new GetItem(existing.Id));
        Assert.Equal((prefix + " Existent actualizat", lookups.UomSets.Single(x => x.Code == "KG").Id, "0.5"), (updated.Name, updated.UomSetId, updated.NetWeightKg));
        var created = Assert.Single((await Send(new SearchItems(ean))).Items);
        Assert.StartsWith("A-", created.Code, StringComparison.Ordinal);

        var export = await Send(new ExportItems(prefix));
        var exported = Sheet.Read(ItemSheet.Definition, export.Content);
        Assert.Equal(2, exported.Count);
        Assert.Contains(exported, r => r["Barcode"] == ean && r["UomSet"] == "BUC" && r["VatRate"] == "S21");

        var errors = Sheet.Read(ItemSheet.Definition, (await Send(new DownloadImportErrors(job.Id))).Content);
        Assert.Equal(5, errors.Count);
    }
}
