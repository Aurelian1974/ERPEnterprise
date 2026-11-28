using Erp.Core.Contracts;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using FluentValidation;

namespace Erp.Inventory.Application.Items;

/// <summary>Setarile modelului pe firma (S1 3.4); Id null = randul lipseste si se arata valorile implicite ale firmei.</summary>
public sealed record ItemCompanyStockDto(int? Id, int? DefaultWarehouseId, ValuationMethod ValuationMethod, TrackingMode TrackingMode, string? MinQty,
    string? MaxQty, ReplenishmentMethod ReplenishmentMethod, bool IsBlockedForIssue, byte[]? RowVersion);

/// <summary>Pragurile si blocarile unui model sau ale unei variante intr-o gestiune (S1 3.4).</summary>
public sealed record ItemWarehouseStockDto(int Id, int WarehouseId, string WarehouseCode, string WarehouseName, int? ItemVariantId, string? VariantCode,
    string? MinQty, string? MaxQty, string? ReorderQty, ReplenishmentMethod? ReplenishmentMethod, bool IsBlockedForIssue, bool IsBlockedForReceipt,
    bool IsListed, byte[] RowVersion);

/// <summary>Tab-ul Stoc al fisei articolului pentru firma curenta (macheta s1-03).</summary>
public sealed record ItemStockTabDto(ItemCompanyStockDto Company, IReadOnlyList<ItemWarehouseStockDto> Warehouses,
    IReadOnlyList<WarehouseOptionDto> Variants, IReadOnlyList<WarehouseOptionDto> DefaultWarehouses);

/// <summary>Accesul la date pentru setarile articolului (inv.usp_ItemStockSettings_*, inv.usp_ItemWarehouseSettings_*).</summary>
public interface IItemStockSettingsStore
{
    Task<ItemStockTabDto> GetAsync(int companyId, int itemId, CancellationToken ct);

    Task<InventorySaved> SaveAsync(int companyId, SaveItemStockSettings settings, int userId, CancellationToken ct);

    Task<InventorySaved> SaveWarehouseAsync(int companyId, SaveItemWarehouseSettings settings, int userId, CancellationToken ct);

    Task DeleteWarehouseAsync(int companyId, int id, byte[] rowVersion, CancellationToken ct);

    Task EnsureDefaultsAsync(int itemId, int userId, CancellationToken ct);
}

/// <summary>Tab-ul Stoc al articolului (S1 3.4).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseView)]
public sealed record GetItemStockSettings(int ItemId) : IQuery<ItemStockTabDto>;

/// <summary>Salveaza setarile modelului pe firma: metoda (doar la prima configurare), urmarirea, gestiunea implicita, pragurile.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.ItemSettingsManage)]
[Audited("Inv.ItemStockSettings.Save")]
public sealed record SaveItemStockSettings(int ItemId, int? DefaultWarehouseId, ValuationMethod ValuationMethod, TrackingMode TrackingMode,
    string? MinQty, string? MaxQty, ReplenishmentMethod? ReplenishmentMethod, bool IsBlockedForIssue, byte[]? RowVersion) : ICommand<InventorySaved>;

/// <summary>Salveaza pragurile si blocarile unui model sau ale unei variante intr-o gestiune.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.ItemSettingsManage)]
[Audited("Inv.ItemWarehouseSettings.Save")]
public sealed record SaveItemWarehouseSettings(int? Id, int WarehouseId, int ItemId, int? ItemVariantId, string? MinQty, string? MaxQty,
    string? ReorderQty, ReplenishmentMethod? ReplenishmentMethod, bool IsBlockedForIssue, bool IsBlockedForReceipt, bool IsListed, byte[]? RowVersion)
    : ICommand<InventorySaved>;

/// <summary>Sterge pragurile pe gestiune; se revine la stratul urmator (model in gestiune, apoi firma).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.ItemSettingsManage)]
[Audited("Inv.ItemWarehouseSettings.Delete")]
public sealed record DeleteItemWarehouseSettings(int Id, byte[] RowVersion) : ICommand<bool>;

internal sealed class SaveItemStockSettingsValidator : AbstractValidator<SaveItemStockSettings>
{
    public SaveItemStockSettingsValidator()
    {
        RuleFor(x => x.ValuationMethod).IsInEnum().WithMessage("Alegeți metoda de evaluare.");
        RuleFor(x => x.TrackingMode).IsInEnum().WithMessage("Alegeți urmărirea pe lot sau serie.");
        RuleFor(x => x.ReplenishmentMethod).IsInEnum().When(x => x.ReplenishmentMethod is not null).WithMessage("Alegeți metoda de reaprovizionare.");
        RuleFor(x => x.MinQty).Must(Quantity.IsValidOptional).WithMessage("Stocul minim este o cantitate pozitivă, cu cel mult șase zecimale.");
        RuleFor(x => x.MaxQty).Must(Quantity.IsValidOptional).WithMessage("Stocul maxim este o cantitate pozitivă, cu cel mult șase zecimale.");
        RuleFor(x => x.MaxQty).Must((x, max) => ItemQty.MaxNotBelowMin(x.MinQty, max)).WithMessage("Stocul maxim este cel puțin egal cu stocul minim.");
    }
}

internal static class ItemQty
{
    public static bool MaxNotBelowMin(string? min, string? max) =>
        Quantity.ParseOptional(min) is not { } a || Quantity.ParseOptional(max) is not { } b || b >= a;
}

internal sealed class SaveItemWarehouseSettingsValidator : AbstractValidator<SaveItemWarehouseSettings>
{
    public SaveItemWarehouseSettingsValidator()
    {
        RuleFor(x => x.ReplenishmentMethod).IsInEnum().When(x => x.ReplenishmentMethod is not null).WithMessage("Alegeți metoda de reaprovizionare.");
        RuleFor(x => x.MinQty).Must(Quantity.IsValidOptional).WithMessage("Stocul minim este o cantitate pozitivă, cu cel mult șase zecimale.");
        RuleFor(x => x.MaxQty).Must(Quantity.IsValidOptional).WithMessage("Stocul maxim este o cantitate pozitivă, cu cel mult șase zecimale.");
        RuleFor(x => x.MaxQty).Must((x, max) => ItemQty.MaxNotBelowMin(x.MinQty, max)).WithMessage("Stocul maxim este cel puțin egal cu stocul minim.");
        RuleFor(x => x.ReorderQty).Must(q => string.IsNullOrWhiteSpace(q) || (Quantity.TryParse(q, out var v) && v > 0))
            .WithMessage("Cantitatea de comandă este pozitivă, cu cel mult șase zecimale.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class GetItemStockSettingsHandler(IItemStockSettingsStore store, IStockCompanySetup setup, ICurrentContext context)
    : IQueryHandler<GetItemStockSettings, ItemStockTabDto>
{
    public async Task<ItemStockTabDto> Handle(GetItemStockSettings request, CancellationToken cancellationToken)
    {
        await setup.EnsureAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        return await store.GetAsync(context.CompanyId, request.ItemId, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ItemStockSettingsHandler(IItemStockSettingsStore store, IStockCompanySetup setup, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveItemStockSettings, InventorySaved>, ICommandHandler<SaveItemWarehouseSettings, InventorySaved>,
    ICommandHandler<DeleteItemWarehouseSettings, bool>
{
    public async Task<InventorySaved> Handle(SaveItemStockSettings request, CancellationToken cancellationToken)
    {
        await setup.EnsureAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var saved = await store.SaveAsync(context.CompanyId, request, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.ItemStockSettings", saved.Id, request with { RowVersion = null });
        return saved;
    }

    public async Task<InventorySaved> Handle(SaveItemWarehouseSettings request, CancellationToken cancellationToken)
    {
        var saved = await store.SaveWarehouseAsync(context.CompanyId, request, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.ItemWarehouseSettings", saved.Id, request with { RowVersion = null });
        return saved;
    }

    public async Task<bool> Handle(DeleteItemWarehouseSettings request, CancellationToken cancellationToken)
    {
        await store.DeleteWarehouseAsync(context.CompanyId, request.Id, request.RowVersion, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.ItemWarehouseSettings", request.Id, new { Deleted = true });
        return true;
    }
}
