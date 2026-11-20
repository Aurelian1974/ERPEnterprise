using System.Globalization;
using Erp.Core.Application.Organization;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;
using Erp.Core.Domain;
using FluentValidation;

namespace Erp.Core.Application.Items;

/// <summary>Un rand din lista articolelor (macheta 05). ItemType: 1 marfa … 8 mijloc fix; VatPercent sir zecimal invariant.</summary>
public sealed record ItemListItemDto(int Id, string Code, string Name, byte ItemType, bool IsStockable, bool IsActive, string VatRateCode, string VatPercent,
    string BaseUomCode, string? GroupName, string? Barcode);

public sealed record ItemPageDto(IReadOnlyList<ItemListItemDto> Items, int Total, int ActiveCount);

/// <summary>BarcodeType: 1 EAN-13, 2 EAN-8, 3 GTIN-14, 4 intern; UomId = UM adusa de scanare (null = UM de baza).</summary>
public sealed record ItemBarcodeDto(int Id, int ItemId, string Barcode, byte BarcodeType, int? UomId, bool IsActive, byte[] RowVersion);

/// <summary>
/// Fisa articolului. Greutatile si volumul sunt siruri zecimale invariante, pe UM de baza; Uoms = exceptiile articolului de la set
/// (au prioritate fata de liniile setului).
/// </summary>
public sealed record ItemDetailDto(int Id, string Code, string Name, byte ItemType, int UomSetId, int BaseUomId, int DefaultVatRateId, int? ItemGroupId,
    byte? PricePrecision, byte? QuantityPrecision, bool IsStockable, string? CnCode, int? DepositItemId, string? DepositItemCode, string? DepositItemName,
    string? NetWeightKg, string? GrossWeightKg, string? VolumeM3, bool IsActive, DateTime CreatedAt, string ModifiedByName, DateTime LastChangedAt,
    byte[] RowVersion, IReadOnlyList<UomLineDto> Uoms, IReadOnlyList<ItemBarcodeDto> Barcodes, IReadOnlyList<PartnerPropertyValue> Properties);

public sealed record ItemSearchCriteria(string? Search, string? Barcode, bool? IsStockable, byte? ItemType, int? ItemGroupId, bool? IsActive,
    int? PropertyDefinitionId, int? PropertyOptionId, bool? PropertyBool, int Offset, int PageSize);

/// <summary>Un articol in exportul Excel (coloanele sablonului de import).</summary>
public sealed record ItemExportRow(string Code, string Name, byte ItemType, bool IsStockable, bool IsActive, string VatRateCode, string BaseUomCode,
    string UomSetCode, string? GroupCode, string? Barcode, byte? PricePrecision, byte? QuantityPrecision, string? CnCode, string? DepositItemCode,
    decimal? NetWeightKg, decimal? GrossWeightKg, decimal? VolumeM3);

/// <summary>Articolul pregatit pentru salvare (cod alocat, valori normalizate). Uoms/Properties null = neschimbate.</summary>
public sealed record ItemRecord(int? Id, string Code, string Name, byte ItemType, int UomSetId, int DefaultVatRateId, int? ItemGroupId, byte? PricePrecision,
    byte? QuantityPrecision, bool IsStockable, string? CnCode, int? DepositItemId, decimal? NetWeightKg, decimal? GrossWeightKg, decimal? VolumeM3,
    bool IsActive, IReadOnlyList<UomLineDto>? Uoms, IReadOnlyList<PartnerPropertyValue>? Properties, byte[]? RowVersion);

public sealed record ExistingItem(int Id, string Code, string Name, byte[] RowVersion);

/// <summary>Articolele si nomenclatoarele lor prin procedurile core.usp_Item*, core.usp_VatRate*, core.usp_UomSet* (NET-06).</summary>
public interface IItemStore
{
    Task<ItemLookupsDto> GetLookupsAsync(int companyId, CancellationToken ct);

    Task<int> SaveVatRatesAsync(IReadOnlyList<VatRateInput> rows, int userId, CancellationToken ct);

    Task<int> SaveUnitsAsync(IReadOnlyList<UnitOfMeasureInput> rows, int userId, CancellationToken ct);

    Task<SavedEntity> SaveUomSetAsync(SaveUomSet command, int userId, CancellationToken ct);

    Task<SavedEntity> SaveItemGroupAsync(SaveItemGroup command, int userId, CancellationToken ct);

    Task<ItemPageDto> SearchAsync(ItemSearchCriteria criteria, CancellationToken ct);

    Task<IReadOnlyList<ItemExportRow>> ExportAsync(ItemSearchCriteria criteria, CancellationToken ct);

    Task<ItemDetailDto?> GetAsync(int id, CancellationToken ct);

    Task<SavedEntity> SaveAsync(ItemRecord item, int userId, CancellationToken ct);

    Task<SavedEntity> SaveBarcodeAsync(SaveItemBarcode command, int userId, CancellationToken ct);

    Task<IReadOnlyList<PartnerHistoryEntryDto>> GetHistoryAsync(int id, CancellationToken ct);

    Task<bool> EnsureCodeSeriesAsync(int companyId, short year, int userId, CancellationToken ct);

    Task<bool> CodeExistsAsync(string code, CancellationToken ct);

    Task<IReadOnlyList<ExistingItem>> FindForImportAsync(IReadOnlyList<string> codes, CancellationToken ct);

    /// <summary>Codurile de bare existente dintre cele date, cu articolul lor.</summary>
    Task<IReadOnlyList<ExistingBarcode>> FindBarcodesAsync(IReadOnlyList<string> barcodes, CancellationToken ct);
}

public sealed record ExistingBarcode(string Barcode, int ItemId, string ItemCode);

/// <summary>Seria codurilor de articol: tipul de document „Core.Item” (nefiscal); fara serie se creeaza A-{An}-{Numar din 6 cifre}.</summary>
public static class ItemCodeSeries
{
    public const string DocumentType = "Core.Item";
    public const string DefaultPrefix = "A";
    public const byte DefaultPadding = 6;
}

/// <summary>
/// Creare sau modificare de articol (02.01, 3.3; macheta 05/05b). Codul gol la creare se aloca din seria „Cod articol” a firmei
/// curente. Uoms = exceptiile de UM ale articolului (null = neschimbate); Properties = valorile proprietatilor (null = neschimbate).
/// Greutatile si volumul: siruri zecimale invariante, pe UM de baza.
/// </summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ItemManage)]
[Audited("Core.Item.Save")]
public sealed record SaveItem(int? Id, string? Code, string Name, byte ItemType, int UomSetId, int DefaultVatRateId, int? ItemGroupId, byte? PricePrecision,
    byte? QuantityPrecision, bool IsStockable, string? CnCode, int? DepositItemId, string? NetWeightKg, string? GrossWeightKg, string? VolumeM3, bool IsActive,
    byte[]? RowVersion, IReadOnlyList<UomLineDto>? Uoms = null, IReadOnlyList<PartnerPropertyValue>? Properties = null) : ICommand<SavedEntity>;

[Module("Core")]
[RequiresPermission(CorePermissions.ItemManage)]
[Audited("Core.ItemBarcode.Save")]
public sealed record SaveItemBarcode(int? Id, int ItemId, string Barcode, byte BarcodeType, int? UomId, bool IsActive, byte[]? RowVersion) : ICommand<SavedEntity>;

/// <summary>Lista articolelor: cautare dupa inceputul denumirii (fara diacritice) sau al codului, ori dupa codul de bare exact.</summary>
[Module("Core")]
[RequiresPermission(CorePermissions.ItemView)]
public sealed record SearchItems(string? Search = null, bool? IsStockable = null, byte? ItemType = null, int? ItemGroupId = null, bool? IsActive = null,
    int? PropertyDefinitionId = null, int? PropertyOptionId = null, bool? PropertyBool = null, int Page = 1, int PageSize = 50) : IQuery<ItemPageDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.ItemView)]
public sealed record GetItem(int Id) : IQuery<ItemDetailDto>;

[Module("Core")]
[RequiresPermission(CorePermissions.ItemView)]
public sealed record GetItemHistory(int Id) : IQuery<IReadOnlyList<PartnerHistoryEntryDto>>;

internal static class ItemRules
{
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static decimal? Measure(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

    public static bool IsMeasure(string? value) => string.IsNullOrWhiteSpace(value) || Decimals.TryParse(value, 6, out _);

    /// <summary>Textul cautat poate fi un cod de bare (cifre, 8–14) sau un cod intern lung: se cauta si exact printre codurile de bare.</summary>
    public static string? BarcodeCandidate(string? search) =>
        search is { Length: >= 4 and <= 50 } && search.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '/' or '-') ? search : null;

    public static ItemSearchCriteria Criteria(SearchItems request)
    {
        var raw = Clean(request.Search);
        var search = raw?.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(request.Page, 1);
        return new ItemSearchCriteria(search, BarcodeCandidate(raw), request.IsStockable, request.ItemType, request.ItemGroupId, request.IsActive,
            request.PropertyDefinitionId, request.PropertyOptionId, request.PropertyBool, (page - 1) * pageSize, pageSize);
    }
}

/// <summary>Codul nou al unui articol din seria „Core.Item”; un numar deja folosit se sare (seria nu e fiscala).</summary>
internal static class ItemCodes
{
    public static async Task<string> NextAsync(IItemStore store, INumberSeriesService series, int companyId, DateOnly today, int userId, CancellationToken ct)
    {
        await store.EnsureCodeSeriesAsync(companyId, (short)today.Year, userId, ct).ConfigureAwait(false);
        for (var attempt = 0; attempt < PartnerCodes.MaxSkips; attempt++)
        {
            var code = (await series.NextAsync(companyId, ItemCodeSeries.DocumentType, today, ct).ConfigureAwait(false)).Formatted;
            if (!await store.CodeExistsAsync(code, ct).ConfigureAwait(false))
            {
                return code;
            }
        }

        throw ErpException.Conflict("CORE_ITEM_DUPLICATE_CODE",
            "Seria „Cod articol” produce doar coduri deja folosite; schimbați prefixul seriei în Configurare › Serii sau introduceți codul.");
    }
}

internal sealed class SaveItemValidator : AbstractValidator<SaveItem>
{
    public SaveItemValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(250).WithMessage("Denumirea are cel mult 250 de caractere.");
        RuleFor(x => x.Code).Matches("^[A-Za-z0-9][A-Za-z0-9._/-]{0,49}$").When(x => !string.IsNullOrWhiteSpace(x.Code))
            .WithMessage("Codul are 1–50 caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Code).NotEmpty().When(x => x.Id is not null).WithMessage("Codul este obligatoriu.");
        RuleFor(x => x.ItemType).InclusiveBetween((byte)1, (byte)8).WithMessage("Alegeți tipul articolului.");
        RuleFor(x => x.UomSetId).GreaterThan(0).WithMessage("Alegeți setul de UM.");
        RuleFor(x => x.DefaultVatRateId).GreaterThan(0).WithMessage("Alegeți cota TVA.");
        RuleFor(x => x.IsStockable).Equal(false).When(x => x.ItemType == (byte)ItemType.Service).WithMessage("Un serviciu nu ține stoc.");
        RuleFor(x => x.PricePrecision).LessThanOrEqualTo((byte)6).WithMessage("Zecimalele prețului sunt între 0 și 6.");
        RuleFor(x => x.QuantityPrecision).LessThanOrEqualTo((byte)6).WithMessage("Zecimalele cantității sunt între 0 și 6.");
        RuleFor(x => x.CnCode).Must(CombinedNomenclature.IsValid).When(x => !string.IsNullOrWhiteSpace(x.CnCode))
            .WithMessage("Codul NC are 4–10 cifre (de exemplu 2201 10 11).");
        RuleFor(x => x.NetWeightKg).Must(ItemRules.IsMeasure).WithMessage("Masa e un număr pozitiv cu cel mult 6 zecimale.");
        RuleFor(x => x.GrossWeightKg).Must(ItemRules.IsMeasure).WithMessage("Masa e un număr pozitiv cu cel mult 6 zecimale.");
        RuleFor(x => x.VolumeM3).Must(ItemRules.IsMeasure).WithMessage("Volumul e un număr pozitiv cu cel mult 6 zecimale.");
        RuleFor(x => x).Must(x => ItemRules.Measure(x.NetWeightKg) is not { } net || ItemRules.Measure(x.GrossWeightKg) is not { } gross || gross >= net)
            .When(x => ItemRules.IsMeasure(x.NetWeightKg) && ItemRules.IsMeasure(x.GrossWeightKg))
            .WithMessage("Masa brută nu e mai mică decât masa netă.").OverridePropertyName("GrossWeightKg");
        RuleForEach(x => x.Uoms).ChildRules(line =>
        {
            line.RuleFor(l => l.FactorToBase).Must(f => Decimals.TryParse(f, 6, out var value) && value > 0)
                .WithMessage("Factorul e un număr pozitiv cu cel mult 6 zecimale.");
            line.RuleFor(l => l.IsBase).Equal(false).WithMessage("UM de bază vine din set.");
        });
        RuleFor(x => x.Uoms).Must(u => u!.Select(x => x.UomId).Distinct().Count() == u!.Count).When(x => x.Uoms is not null)
            .WithMessage("Fiecare UM apare o singură dată.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveItemHandler(IItemStore store, INumberSeriesService series, ICurrentContext context, IAuditScope audit, IOutbox outbox, TimeProvider clock)
    : ICommandHandler<SaveItem, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveItem request, CancellationToken cancellationToken)
    {
        var lookups = await store.GetLookupsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        if (request.Properties is not null)
        {
            PartnerPropertyRules.Validate(request.Properties, lookups.PropertyLookups, _ => true);
        }

        var code = ItemRules.Clean(request.Code);
        if (code is null)
        {
            if (context.CompanyId == 0)
            {
                throw new ErpValidationException(new Dictionary<string, string[]>
                {
                    ["code"] = ["Alegeți firma curentă (seria de coduri a articolelor e pe firmă) sau introduceți codul."],
                });
            }

            code = await ItemCodes.NextAsync(store, series, context.CompanyId, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), context.UserId,
                cancellationToken).ConfigureAwait(false);
        }

        var item = new ItemRecord(request.Id, code, request.Name.Trim(), request.ItemType, request.UomSetId, request.DefaultVatRateId, request.ItemGroupId,
            request.PricePrecision, request.QuantityPrecision, request.IsStockable,
            ItemRules.Clean(request.CnCode) is { } cn ? CombinedNomenclature.Normalize(cn) : null, request.DepositItemId,
            ItemRules.Measure(request.NetWeightKg), ItemRules.Measure(request.GrossWeightKg), ItemRules.Measure(request.VolumeM3), request.IsActive,
            request.Uoms, request.Properties, request.RowVersion);
        var saved = await store.SaveAsync(item, context.UserId, cancellationToken).ConfigureAwait(false);

        var vat = lookups.VatRates.First(v => v.Id == item.DefaultVatRateId);
        var baseUom = lookups.Units.First(u => u.Id == lookups.UomSets.First(s => s.Id == item.UomSetId).Lines.First(l => l.IsBase).UomId);
        audit.Describe("Core.Item", saved.Id, new
        {
            item.Code, item.Name, item.ItemType, item.UomSetId, item.DefaultVatRateId, item.ItemGroupId, item.IsStockable, item.CnCode, item.DepositItemId,
            item.IsActive, UomsChanged = item.Uoms is null ? (bool?)null : true, PropertiesChanged = item.Properties is null ? (bool?)null : true,
        });
        await outbox.EnqueueAsync(new ArticolModificat(saved.Id, item.Code, item.Name, item.ItemType, baseUom.Code, vat.Code, item.DepositItemId, item.IsActive)
        {
            CompanyId = context.CompanyId,
        }, cancellationToken).ConfigureAwait(false);
        return saved;
    }
}

internal sealed class SaveItemBarcodeValidator : AbstractValidator<SaveItemBarcode>
{
    public SaveItemBarcodeValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0);
        RuleFor(x => x.BarcodeType).InclusiveBetween((byte)1, (byte)4).WithMessage("Alegeți tipul codului de bare.");
        RuleFor(x => x.Barcode).Must((x, code) => Barcodes.IsValid(x.BarcodeType, code?.Trim()))
            .WithMessage("Codul de bare nu e valid pentru tipul ales (lungimea sau cifra de control GS1).");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SaveItemBarcodeHandler(IItemStore store, ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveItemBarcode, SavedEntity>
{
    public async Task<SavedEntity> Handle(SaveItemBarcode request, CancellationToken cancellationToken)
    {
        var command = request with { Barcode = request.Barcode.Trim() };
        var saved = await store.SaveBarcodeAsync(command, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Core.ItemBarcode", saved.Id, new { command.ItemId, command.Barcode, command.BarcodeType, command.UomId, command.IsActive });
        return saved;
    }
}

internal sealed class SearchItemsHandler(IItemStore store) : IQueryHandler<SearchItems, ItemPageDto>
{
    public Task<ItemPageDto> Handle(SearchItems request, CancellationToken cancellationToken) => store.SearchAsync(ItemRules.Criteria(request), cancellationToken);
}

internal sealed class GetItemHandler(IItemStore store) : IQueryHandler<GetItem, ItemDetailDto>
{
    public async Task<ItemDetailDto> Handle(GetItem request, CancellationToken cancellationToken) =>
        await store.GetAsync(request.Id, cancellationToken).ConfigureAwait(false) ?? throw ErpException.NotFound("CORE_ENTITY_NOT_FOUND", "Articolul nu există.");
}

internal sealed class GetItemHistoryHandler(IItemStore store) : IQueryHandler<GetItemHistory, IReadOnlyList<PartnerHistoryEntryDto>>
{
    public Task<IReadOnlyList<PartnerHistoryEntryDto>> Handle(GetItemHistory request, CancellationToken cancellationToken) =>
        store.GetHistoryAsync(request.Id, cancellationToken);
}
