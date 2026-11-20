using System.Globalization;
using System.Text.Json;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;
using Erp.Core.Domain;
using FluentValidation;

namespace Erp.Core.Application.Items;

/// <summary>
/// Coloanele exportului si ale sablonului de import articole (02.01, 4.8): datele generale, setul de UM si cota TVA dupa cod,
/// un cod de bare. Exceptiile de UM, celelalte coduri de bare si proprietatile se completeaza din fisa.
/// </summary>
public static class ItemSheet
{
    public const string SheetName = "Articole";

    public static readonly IReadOnlyList<PartnerSheetColumn> Columns =
    [
        new("Code", "Cod", false, "APA05"),
        new("Name", "Denumire", true, "Apă minerală naturală carbogazoasă 0,5 L"),
        new("ItemType", "Tip", true, "Marfă"),
        new("IsStockable", "Stocabil", false, "Da"),
        new("UomSet", "Set UM (cod)", true, "BAUT05"),
        new("VatRate", "Cotă TVA (cod)", true, "R11"),
        new("ItemGroup", "Grupă (cod)", false, ""),
        new("Barcode", "Cod de bare", false, "5941234500011"),
        new("CnCode", "Cod NC", false, "22011011"),
        new("DepositItem", "Garanție SGR (cod articol)", false, ""),
        new("PricePrecision", "Zecimale preț", false, ""),
        new("QuantityPrecision", "Zecimale cantitate", false, ""),
        new("NetWeightKg", "Masă netă (kg)", false, "0,5"),
        new("GrossWeightKg", "Masă brută (kg)", false, "0,523"),
        new("VolumeM3", "Volum (m³)", false, ""),
        new("IsActive", "Activ", false, "Da"),
    ];

    public static readonly IReadOnlyList<string> TypeNames =
        ["Marfă", "Materie primă", "Material", "Produs finit", "Semifabricat", "Serviciu", "Ambalaj", "Mijloc fix"];

    public static readonly SheetDefinition Definition = new(SheetName, Columns);

    public static string Header(string key) => Definition.Header(key);

    public static string TypeName(byte type) => type is >= 1 and <= 8 ? TypeNames[type - 1] : type.ToString(CultureInfo.InvariantCulture);

    /// <summary>Tipul dupa nume (fara diacritice) sau cifra 1–8.</summary>
    public static byte? Type(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var folded = PartnerSheet.Fold(value);
        for (var i = 0; i < TypeNames.Count; i++)
        {
            if (PartnerSheet.Fold(TypeNames[i]) == folded || folded == (i + 1).ToString(CultureInfo.InvariantCulture))
            {
                return (byte)(i + 1);
            }
        }

        return null;
    }

    /// <summary>Tipul codului de bare dupa forma: 13, 8 sau 14 cifre = EAN-13, EAN-8, GTIN-14; altfel intern.</summary>
    public static byte BarcodeType(string barcode) => !barcode.All(char.IsAsciiDigit) ? (byte)4 : barcode.Length switch
    {
        13 => (byte)1,
        8 => (byte)2,
        14 => (byte)3,
        _ => (byte)4,
    };

    public static IReadOnlyList<SheetList> Lists(ItemLookupsDto lookups) =>
    [
        new(Header("ItemType"), TypeNames, ["ItemType"]),
        new("Da / Nu", ["Da", "Nu"], ["IsStockable", "IsActive"]),
        new(Header("UomSet"), [.. lookups.UomSets.Where(s => s.IsActive).Select(s => s.Code)], ["UomSet"]),
        new(Header("VatRate"), [.. lookups.VatRates.Where(v => v.ValidTo is null).Select(v => v.Code)], ["VatRate"]),
        new(Header("ItemGroup"), [.. lookups.Groups.Where(g => g.IsActive).Select(g => g.Code)], ["ItemGroup"]),
    ];

    /// <summary>Numar cu virgula sau punct zecimal, in forma invarianta a API-ului; null = gol.</summary>
    public static string? Number(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace(',', '.');
}

/// <summary>Un rand valid, normalizat, al importului de articole.</summary>
public sealed record ItemImportRow(int RowNumber, string? Code, string Name, byte ItemType, bool IsStockable, int UomSetId, int DefaultVatRateId,
    int? ItemGroupId, string? Barcode, string? CnCode, int? DepositItemId, byte? PricePrecision, byte? QuantityPrecision, string? NetWeightKg,
    string? GrossWeightKg, string? VolumeM3, bool IsActive);

public sealed record ItemImportAnalysis(string FileName, int TotalRows, IReadOnlyList<ItemImportRow> ValidRows, ImportReport Report)
{
    public int WarningRows => Report.Warnings.Where(w => w.Row > 0).Select(w => w.Row).Distinct().Count();
}

[Module("Core")]
[RequiresPermission(CorePermissions.ItemView)]
public sealed record ExportItems(string? Search = null, bool? IsStockable = null, byte? ItemType = null, int? ItemGroupId = null, bool? IsActive = null,
    int? PropertyDefinitionId = null, int? PropertyOptionId = null, bool? PropertyBool = null) : IQuery<FileDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
public sealed record GetItemImportTemplate : IQuery<FileDto>;

/// <summary>Validarea unui fisier de articole (4.8 pasii 2–3), fara scrieri; jobul se creeaza cu <see cref="CreateItemImportJob"/>.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
public sealed record AnalyzeItemImport(string FileName, string FileBase64) : IQuery<ItemImportAnalysis>;

[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
[Audited("Core.Import.Validate")]
public sealed record CreateItemImportJob(ItemImportAnalysis Analysis) : ICommand<ImportJobDto>;

/// <summary>Urmatorul lot de cel mult 500 de articole, in tranzactia lui; acelasi cod actualizeaza articolul existent.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ImportRun)]
[Audited("Core.Item.Import")]
public sealed record ImportItemBatch(int JobId) : ICommand<ImportBatchResult>;

internal sealed class ExportItemsHandler(IItemStore store, IPartnerSpreadsheet spreadsheet, ICurrentContext context, TimeProvider clock)
    : IQueryHandler<ExportItems, FileDto>
{
    public async Task<FileDto> Handle(ExportItems request, CancellationToken cancellationToken)
    {
        var criteria = ItemRules.Criteria(new SearchItems(request.Search, request.IsStockable, request.ItemType, request.ItemGroupId, request.IsActive,
            request.PropertyDefinitionId, request.PropertyOptionId, request.PropertyBool));
        var rows = await store.ExportAsync(criteria, cancellationToken).ConfigureAwait(false);
        string? Number(decimal? value) => value is { } v ? Decimals.Trim(v) : null;
        var values = rows.Select(r => (IReadOnlyList<string?>)
        [
            r.Code, r.Name, ItemSheet.TypeName(r.ItemType), PartnerSheet.YesNoText(r.IsStockable), r.UomSetCode, r.VatRateCode, r.GroupCode, r.Barcode,
            r.CnCode, r.DepositItemCode, r.PricePrecision?.ToString(CultureInfo.InvariantCulture), r.QuantityPrecision?.ToString(CultureInfo.InvariantCulture),
            Number(r.NetWeightKg), Number(r.GrossWeightKg), Number(r.VolumeM3), PartnerSheet.YesNoText(r.IsActive),
        ]).ToList();
        var lookups = await store.GetLookupsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var stamp = clock.GetLocalNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new FileDto($"articole-{stamp}.xlsx", FileDto.Xlsx, spreadsheet.Write(ItemSheet.Definition, values, ItemSheet.Lists(lookups)));
    }
}

internal sealed class GetItemImportTemplateHandler(IItemStore store, IPartnerSpreadsheet spreadsheet, ICurrentContext context)
    : IQueryHandler<GetItemImportTemplate, FileDto>
{
    public async Task<FileDto> Handle(GetItemImportTemplate request, CancellationToken cancellationToken)
    {
        IReadOnlyList<string?> example = [.. ItemSheet.Columns.Select(c => (string?)(c.Example.Length == 0 ? null : c.Example))];
        var lookups = await store.GetLookupsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        return new FileDto("sablon-import-articole.xlsx", FileDto.Xlsx, spreadsheet.Write(ItemSheet.Definition, [example], ItemSheet.Lists(lookups)));
    }
}

internal sealed class AnalyzeItemImportHandler(IPartnerSpreadsheet spreadsheet, IItemStore store, IValidator<SaveItem> validator, ICurrentContext context)
    : IQueryHandler<AnalyzeItemImport, ItemImportAnalysis>
{
    // Campurile comenzii SaveItem si coloanele foii, ca erorile validatorului sa arate coloana din Excel.
    private static readonly Dictionary<string, string> FieldColumns = new(StringComparer.Ordinal)
    {
        ["Code"] = "Code", ["Name"] = "Name", ["ItemType"] = "ItemType", ["IsStockable"] = "IsStockable", ["UomSetId"] = "UomSet",
        ["DefaultVatRateId"] = "VatRate", ["ItemGroupId"] = "ItemGroup", ["CnCode"] = "CnCode", ["DepositItemId"] = "DepositItem",
        ["PricePrecision"] = "PricePrecision", ["QuantityPrecision"] = "QuantityPrecision", ["NetWeightKg"] = "NetWeightKg",
        ["GrossWeightKg"] = "GrossWeightKg", ["VolumeM3"] = "VolumeM3", ["IsActive"] = "IsActive",
    };

    public async Task<ItemImportAnalysis> Handle(AnalyzeItemImport request, CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.FileBase64);
        }
        catch (FormatException)
        {
            throw File("Fișierul nu a putut fi citit.");
        }

        if (content.Length > ImportJson.MaxFileBytes)
        {
            throw File("Fișierul depășește 10 MB; împărțiți-l în mai multe importuri.");
        }

        var sheet = spreadsheet.Read(ItemSheet.Definition, content);
        if (sheet.Count == 0)
        {
            throw File("Fișierul nu are rânduri de importat (sub antet).");
        }

        var lookups = await store.GetLookupsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var depositCodes = sheet.Select(r => r["DepositItem"]).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var deposits = await store.FindForImportAsync(depositCodes, cancellationToken).ConfigureAwait(false);
        var errors = new List<ImportIssueDto>();
        var warnings = new List<ImportIssueDto>();
        var valid = new List<(PartnerSheetRow Row, SaveItem Command, string? Barcode)>();
        foreach (var row in sheet)
        {
            var issues = new List<ImportIssueDto>();
            var parsed = Parse(row, lookups, deposits, issues);
            if (parsed is { } command && issues.Count == 0)
            {
                var result = await validator.ValidateAsync(command, cancellationToken).ConfigureAwait(false);
                foreach (var failure in result.Errors)
                {
                    var key = FieldColumns.GetValueOrDefault(failure.PropertyName, "Name");
                    issues.Add(new ImportIssueDto(row.RowNumber, ItemSheet.Header(key), row[key], failure.ErrorMessage));
                }
            }

            var barcode = row["Barcode"];
            if (barcode is not null && !Barcodes.IsValid(ItemSheet.BarcodeType(barcode), barcode))
            {
                issues.Add(new ImportIssueDto(row.RowNumber, ItemSheet.Header("Barcode"), barcode, "Codul de bare nu e valid (lungimea sau cifra de control GS1)."));
            }

            if (issues.Count == 0 && parsed is not null)
            {
                valid.Add((row, parsed, barcode));
            }

            errors.AddRange(issues);
        }

        valid = CheckDuplicatesInFile(valid, errors);
        var existing = await store.FindForImportAsync([.. valid.Select(v => ItemRules.Clean(v.Command.Code)).OfType<string>()], cancellationToken)
            .ConfigureAwait(false);
        var barcodes = await store.FindBarcodesAsync([.. valid.Select(v => v.Barcode).OfType<string>()], cancellationToken).ConfigureAwait(false);
        var rows = new List<ItemImportRow>();
        foreach (var (row, command, barcode) in valid)
        {
            var code = ItemRules.Clean(command.Code);
            var match = code is null ? null : existing.FirstOrDefault(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase));
            var owner = barcode is null ? null : barcodes.FirstOrDefault(b => b.Barcode == barcode);
            if (owner is not null && owner.ItemId != match?.Id)
            {
                errors.Add(new ImportIssueDto(row.RowNumber, ItemSheet.Header("Barcode"), barcode, $"Codul de bare e folosit la articolul {owner.ItemCode}."));
                continue;
            }

            if (match is null && code is null && context.CompanyId == 0)
            {
                errors.Add(new ImportIssueDto(row.RowNumber, ItemSheet.Header("Code"), null,
                    "Codul e obligatoriu la nivelul instalării; alegeți firma curentă ca să se aloce din serie."));
                continue;
            }

            if (match is not null)
            {
                warnings.Add(new ImportIssueDto(row.RowNumber, ItemSheet.Header("Code"), row["Code"], $"Există deja articolul „{match.Name}”; se actualizează."));
            }

            rows.Add(new ItemImportRow(row.RowNumber, code, command.Name.Trim(), command.ItemType, command.IsStockable, command.UomSetId, command.DefaultVatRateId,
                command.ItemGroupId, barcode, ItemRules.Clean(command.CnCode) is { } cn ? CombinedNomenclature.Normalize(cn) : null, command.DepositItemId,
                command.PricePrecision, command.QuantityPrecision, command.NetWeightKg, command.GrossWeightKg, command.VolumeM3, command.IsActive));
        }

        var errorRows = errors.GroupBy(e => e.Row).OrderBy(g => g.Key)
            .Select(g => new ImportErrorRow(g.Key, sheet.First(r => r.RowNumber == g.Key).Values, string.Join("; ", g.Select(e => $"{e.Column}: {e.Message}"))))
            .ToList();
        return new ItemImportAnalysis(request.FileName, sheet.Count, rows, new ImportReport([.. errors.OrderBy(e => e.Row)], [.. warnings], errorRows));
    }

    private static SaveItem? Parse(PartnerSheetRow row, ItemLookupsDto lookups, IReadOnlyList<ExistingItem> deposits, List<ImportIssueDto> issues)
    {
        void Issue(string key, string message) => issues.Add(new ImportIssueDto(row.RowNumber, ItemSheet.Header(key), row[key], message));

        bool YesNo(string key, bool fallback)
        {
            var value = PartnerSheet.YesNo(row[key], out var recognized);
            if (!recognized)
            {
                Issue(key, "Valoare nepermisă; scrieți Da sau Nu.");
            }

            return value ?? fallback;
        }

        byte? Precision(string key)
        {
            var text = row[key];
            if (text is null)
            {
                return null;
            }

            if (byte.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= 6)
            {
                return value;
            }

            Issue(key, "Zecimalele sunt un număr între 0 și 6.");
            return null;
        }

        var type = ItemSheet.Type(row["ItemType"]);
        if (type is null)
        {
            Issue("ItemType", row["ItemType"] is null ? "Câmp obligatoriu." : "Valoare nepermisă; alegeți din listă (" + string.Join(", ", ItemSheet.TypeNames) + ").");
        }

        var stockable = YesNo("IsStockable", type is not (byte)ItemType.Service);
        var active = YesNo("IsActive", true);
        var set = lookups.UomSets.FirstOrDefault(s => s.IsActive && string.Equals(s.Code, row["UomSet"], StringComparison.OrdinalIgnoreCase));
        if (set is null)
        {
            Issue("UomSet", row["UomSet"] is null ? "Câmp obligatoriu." : "Set de UM necunoscut sau inactiv; folosiți codul din foaia „Liste”.");
        }

        var vat = lookups.VatRates.FirstOrDefault(v => v.ValidTo is null && string.Equals(v.Code, row["VatRate"], StringComparison.OrdinalIgnoreCase));
        if (vat is null)
        {
            Issue("VatRate", row["VatRate"] is null ? "Câmp obligatoriu." : "Cotă TVA necunoscută sau care nu mai e în vigoare; folosiți codul din foaia „Liste”.");
        }

        int? groupId = null;
        if (row["ItemGroup"] is { } groupCode)
        {
            groupId = lookups.Groups.FirstOrDefault(g => g.IsActive && string.Equals(g.Code, groupCode, StringComparison.OrdinalIgnoreCase))?.Id;
            if (groupId is null)
            {
                Issue("ItemGroup", "Grupă necunoscută sau inactivă; folosiți codul din foaia „Liste”.");
            }
        }

        int? depositId = null;
        if (row["DepositItem"] is { } depositCode)
        {
            depositId = deposits.FirstOrDefault(d => string.Equals(d.Code, depositCode, StringComparison.OrdinalIgnoreCase))?.Id;
            if (depositId is null)
            {
                Issue("DepositItem", "Articolul de garanție SGR nu există (se importă întâi ambalajul).");
            }
        }

        var price = Precision("PricePrecision");
        var quantity = Precision("QuantityPrecision");
        if (issues.Count > 0 || type is null || set is null || vat is null)
        {
            return null;
        }

        return new SaveItem(null, row["Code"], row["Name"] ?? string.Empty, type.Value, set.Id, vat.Id, groupId, price, quantity, stockable, row["CnCode"],
            depositId, ItemSheet.Number(row["NetWeightKg"]), ItemSheet.Number(row["GrossWeightKg"]), ItemSheet.Number(row["VolumeM3"]), active, null);
    }

    private static List<(PartnerSheetRow Row, SaveItem Command, string? Barcode)> CheckDuplicatesInFile(
        List<(PartnerSheetRow Row, SaveItem Command, string? Barcode)> rows, List<ImportIssueDto> errors)
    {
        var codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var barcodes = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<(PartnerSheetRow Row, SaveItem Command, string? Barcode)>();
        foreach (var entry in rows)
        {
            var code = ItemRules.Clean(entry.Command.Code);
            if (code is not null && codes.TryGetValue(code, out var first))
            {
                errors.Add(new ImportIssueDto(entry.Row.RowNumber, ItemSheet.Header("Code"), code, $"Codul apare și pe rândul {first}."));
                continue;
            }

            if (entry.Barcode is not null && barcodes.TryGetValue(entry.Barcode, out var firstBarcode))
            {
                errors.Add(new ImportIssueDto(entry.Row.RowNumber, ItemSheet.Header("Barcode"), entry.Barcode, $"Codul de bare apare și pe rândul {firstBarcode}."));
                continue;
            }

            if (code is not null)
            {
                codes[code] = entry.Row.RowNumber;
            }

            if (entry.Barcode is not null)
            {
                barcodes[entry.Barcode] = entry.Row.RowNumber;
            }

            result.Add(entry);
        }

        return result;
    }

    private static ErpValidationException File(string message) => new(new Dictionary<string, string[]> { ["file"] = [message] });
}

internal sealed class CreateItemImportJobHandler(IImportJobStore jobs, ICurrentContext context, IAuditScope audit) : ICommandHandler<CreateItemImportJob, ImportJobDto>
{
    public async Task<ImportJobDto> Handle(CreateItemImportJob request, CancellationToken cancellationToken)
    {
        var analysis = request.Analysis;
        var saved = await jobs.CreateAsync(new NewImportJob("Item", context.CompanyId, analysis.FileName, false, analysis.TotalRows, analysis.ValidRows.Count,
            analysis.Report.ErrorRows.Count, analysis.WarningRows, JsonSerializer.Serialize(analysis.Report, ImportJson.Options),
            JsonSerializer.Serialize(analysis.ValidRows, ImportJson.Options)), context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ImportJob", saved.Id, new { EntityType = "Item", analysis.FileName, analysis.TotalRows, ValidRows = analysis.ValidRows.Count });
        var record = await jobs.GetAsync(saved.Id, forUpdate: false, withData: true, cancellationToken).ConfigureAwait(false) ?? throw ImportJson.NotFound();
        return ImportJson.WithReport(record);
    }
}

internal sealed class ImportItemBatchHandler(IImportJobStore jobs, IItemStore items, INumberSeriesService series, ICurrentContext context, IAuditScope audit,
    IOutbox outbox, TimeProvider clock) : ICommandHandler<ImportItemBatch, ImportBatchResult>
{
    public async Task<ImportBatchResult> Handle(ImportItemBatch request, CancellationToken cancellationToken)
    {
        var record = await jobs.GetAsync(request.JobId, forUpdate: true, withData: true, cancellationToken).ConfigureAwait(false) ?? throw ImportJson.NotFound();
        var job = record.Job;
        if (job.EntityType != "Item" || job.Status != ImportStatus.Validated)
        {
            throw ErpException.Conflict("CORE_IMPORT_NOT_PENDING", "Importul a fost deja confirmat, anulat sau încheiat.");
        }

        var rows = JsonSerializer.Deserialize<List<ItemImportRow>>(record.Payload ?? "[]", ImportJson.Options) ?? [];
        var batch = rows.Skip(job.ImportedRows).Take(ImportJson.BatchSize).ToList();
        var existing = await items.FindForImportAsync([.. batch.Select(r => r.Code).OfType<string>()], cancellationToken).ConfigureAwait(false);
        var barcodes = await items.FindBarcodesAsync([.. batch.Select(r => r.Barcode).OfType<string>()], cancellationToken).ConfigureAwait(false);
        var lookups = await items.GetLookupsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        foreach (var row in batch)
        {
            var match = row.Code is null ? null : existing.FirstOrDefault(e => string.Equals(e.Code, row.Code, StringComparison.OrdinalIgnoreCase));
            var code = row.Code ?? await ItemCodes.NextAsync(items, series, record.CompanyId ?? context.CompanyId, today, context.UserId, cancellationToken)
                .ConfigureAwait(false);
            var item = new ItemRecord(match?.Id, code, row.Name, row.ItemType, row.UomSetId, row.DefaultVatRateId, row.ItemGroupId, row.PricePrecision,
                row.QuantityPrecision, row.IsStockable, row.CnCode, row.DepositItemId, ItemRules.Measure(row.NetWeightKg), ItemRules.Measure(row.GrossWeightKg),
                ItemRules.Measure(row.VolumeM3), row.IsActive, null, null, match?.RowVersion);
            var saved = await items.SaveAsync(item, context.UserId, cancellationToken).ConfigureAwait(false);
            audit.Describe("Core.Item", saved.Id, new { ImportJobId = job.Id, row.RowNumber, Updated = match is not null, item.Code, item.Name, item.ItemType, item.IsActive });

            var owner = row.Barcode is null ? null : barcodes.FirstOrDefault(b => b.Barcode == row.Barcode);
            if (row.Barcode is not null && owner is null)
            {
                var barcode = await items.SaveBarcodeAsync(new SaveItemBarcode(null, saved.Id, row.Barcode, ItemSheet.BarcodeType(row.Barcode), null, true, null),
                    context.UserId, cancellationToken).ConfigureAwait(false);
                audit.Describe("Core.ItemBarcode", barcode.Id, new { ItemId = saved.Id, row.Barcode });
            }
            else if (owner is not null && owner.ItemId != saved.Id)
            {
                throw ErpException.Conflict("CORE_IMPORT_CONFLICT", $"Rândul {row.RowNumber}: codul de bare e folosit între timp la articolul {owner.ItemCode}.");
            }

            var vat = lookups.VatRates.First(v => v.Id == row.DefaultVatRateId);
            var baseUom = lookups.Units.First(u => u.Id == lookups.UomSets.First(s => s.Id == row.UomSetId).Lines.First(l => l.IsBase).UomId);
            await outbox.EnqueueAsync(new ArticolModificat(saved.Id, code, row.Name, row.ItemType, baseUom.Code, vat.Code, row.DepositItemId, row.IsActive)
            {
                CompanyId = context.CompanyId,
            }, cancellationToken).ConfigureAwait(false);
        }

        var imported = job.ImportedRows + batch.Count;
        var done = imported >= rows.Count;
        await jobs.UpdateAsync(job.Id, done ? ImportStatus.Imported : ImportStatus.Validated, imported, null, done, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        return new ImportBatchResult(imported, done);
    }
}
