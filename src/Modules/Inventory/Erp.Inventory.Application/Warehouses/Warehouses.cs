using Erp.Core.Contracts;
using Erp.Inventory.Contracts;
using FluentValidation;

namespace Erp.Inventory.Application.Warehouses;

/// <summary>O gestiune in lista ecranului Gestiuni (macheta s1-01).</summary>
public sealed record WarehouseItemDto(int Id, string Code, string Name, WarehouseKind Kind, ValuationBasis ValuationBasis, int? LocationId,
    string? LocationName, int? ThirdPartyId, string? ThirdPartyName, NegativeStockPolicy NegativeStockPolicy, WarehousePurpose Purpose,
    int DimensionValueId, bool IsBlocked, bool IsActive);

/// <summary>Fisa gestiunii (macheta s1-01, tab-ul Date generale).</summary>
public sealed record WarehouseDetailDto(int Id, int CompanyId, string Code, string Name, int? LocationId, string? LocationName, WarehouseKind Kind,
    ValuationBasis ValuationBasis, int? RetailPriceListId, bool IsOwnGoods, int? ThirdPartyId, string? ThirdPartyName,
    NegativeStockPolicy NegativeStockPolicy, int DimensionValueId, int? DefaultCostCenterId, bool IsBlocked, string? BlockReason, DateTime? BlockedAt,
    byte DeliveryOrderMode, int? ReturnWarehouseId, WarehousePurpose Purpose, bool IsActive, bool HasMovements, DateTime CreatedAt,
    DateTime? ModifiedAt, string? ModifiedByName, byte[] RowVersion);

/// <summary>O optiune de lista (locatie, centru de cost, gestiune de retur).</summary>
public sealed record WarehouseOptionDto(int Id, string Code, string Name);

/// <summary>O locatie a firmei, cu tipul ei (Nucleu, core.Location).</summary>
public sealed record WarehouseLocationDto(int Id, string Code, string Name, byte LocationType);

public sealed record WarehouseLookupsDto(IReadOnlyList<WarehouseLocationDto> Locations, IReadOnlyList<WarehouseOptionDto> CostCenters,
    IReadOnlyList<WarehouseOptionDto> ReturnWarehouses);

public sealed record WarehousePageDto(IReadOnlyList<WarehouseItemDto> Warehouses, WarehouseLookupsDto Lookups);

public sealed record WarehouseRecord(int? Id, int CompanyId, string Code, string Name, int? LocationId, WarehouseKind Kind, ValuationBasis ValuationBasis,
    int? RetailPriceListId, int? ThirdPartyId, NegativeStockPolicy? NegativeStockPolicy, int? DimensionValueId, int? DefaultCostCenterId,
    byte DeliveryOrderMode, int? ReturnWarehouseId, WarehousePurpose Purpose, byte[]? RowVersion);

/// <summary>Starea gestiunii dupa o scriere: versiunea, valoarea de dimensiune si activarea (pentru alinierea dimensiunii).</summary>
public sealed record WarehouseWritten(int Id, byte[] RowVersion, int DimensionValueId, bool IsActive);

/// <summary>Accesul la date pentru gestiuni (inv.usp_Warehouse_*, NET-06).</summary>
public interface IWarehouseStore
{
    Task<IReadOnlyList<WarehouseItemDto>> ListAsync(int companyId, string? search, WarehouseKind? kind, int? locationId, bool includeInactive,
        CancellationToken ct);

    Task<WarehouseLookupsDto> GetLookupsAsync(int companyId, CancellationToken ct);

    Task<WarehouseDetailDto?> GetAsync(int companyId, int id, CancellationToken ct);

    Task<WarehouseWritten> SaveAsync(WarehouseRecord warehouse, int userId, CancellationToken ct);

    Task<WarehouseWritten> SetBlockedAsync(int companyId, int id, bool isBlocked, string? reason, byte[]? rowVersion, int userId, CancellationToken ct);

    Task<WarehouseWritten> SetActiveAsync(int companyId, int id, bool isActive, byte[] rowVersion, int userId, CancellationToken ct);

    Task<IReadOnlyList<InventoryHistoryEntryDto>> HistoryAsync(int companyId, int id, CancellationToken ct);
}

/// <summary>Initializarea firmei in Stocuri (S1 5.2): setarile implicite si gestiunea TRANZIT, idempotent.</summary>
public interface IStockCompanySetup
{
    Task EnsureAsync(int companyId, CancellationToken ct);
}

/// <summary>Lista ecranului Gestiuni (S1 11), restransa la gestiunile permise utilizatorului (S1 3.7), cu listele fisei.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseView)]
public sealed record GetWarehouses(string? Search = null, WarehouseKind? Kind = null, int? LocationId = null, bool IncludeInactive = true)
    : IQuery<WarehousePageDto>;

/// <summary>Fisa unei gestiuni a firmei curente.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseView)]
public sealed record GetWarehouse(int Id) : IQuery<WarehouseDetailDto>;

/// <summary>Tab-ul Istoric al gestiunii (macheta s1-01).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseView)]
public sealed record GetWarehouseHistory(int Id) : IQuery<IReadOnlyList<InventoryHistoryEntryDto>>;

/// <summary>
/// Creeaza sau modifica o gestiune a firmei curente (S1 3.1, 4.1): la creare, valoarea de dimensiune „gestiune” cu acelasi cod si aceeasi
/// denumire, in aceeasi tranzactie; la modificare, valoarea se aliniaza. Se publica GestiuneModificata.
/// </summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseManage)]
[Audited("Inv.Warehouse.Save")]
public sealed record SaveWarehouse(int? Id, string Code, string Name, int? LocationId, WarehouseKind Kind, ValuationBasis ValuationBasis,
    int? RetailPriceListId, int? ThirdPartyId, NegativeStockPolicy? NegativeStockPolicy, int? DefaultCostCenterId, byte DeliveryOrderMode,
    int? ReturnWarehouseId, WarehousePurpose Purpose, byte[]? RowVersion) : ICommand<InventorySaved>;

/// <summary>Blocarea temporara a gestiunii, cu motiv (S1 4.3).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseBlock)]
[Audited("Inv.Warehouse.Block")]
public sealed record BlockWarehouse(int Id, string Reason, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Deblocarea gestiunii (S1 4.3).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseBlock)]
[Audited("Inv.Warehouse.Unblock")]
public sealed record UnblockWarehouse(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Dezactivarea gestiunii (S1 4.4): stoc zero, fara ciorne si rezervari (54006); dimensiunea se dezactiveaza si ea.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseManage)]
[Audited("Inv.Warehouse.Deactivate")]
public sealed record DeactivateWarehouse(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Reactivarea gestiunii (S1 4.4).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseManage)]
[Audited("Inv.Warehouse.Reactivate")]
public sealed record ReactivateWarehouse(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

internal sealed class SaveWarehouseValidator : AbstractValidator<SaveWarehouse>
{
    public SaveWarehouseValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.")
            .Matches("^[A-Za-z0-9][A-Za-z0-9._/-]{0,19}$").WithMessage("Codul are 1–20 de caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200).WithMessage("Denumirea are cel mult 200 de caractere.");
        RuleFor(x => x.Kind).IsInEnum().WithMessage("Alegeți tipul gestiunii.");
        RuleFor(x => x.Kind).NotEqual(WarehouseKind.Transit).When(x => x.Id is null)
            .WithMessage("Gestiunea de tranzit se creează automat, una pe firmă.");
        RuleFor(x => x.ValuationBasis).IsInEnum().WithMessage("Alegeți baza de evaluare.");
        RuleFor(x => x.NegativeStockPolicy).IsInEnum().When(x => x.NegativeStockPolicy is not null).WithMessage("Alegeți politica de stoc negativ.");
        RuleFor(x => x.Purpose).IsInEnum().WithMessage("Alegeți destinația gestiunii.");
        RuleFor(x => x.DeliveryOrderMode).InclusiveBetween((byte)0, (byte)2).WithMessage("Alegeți modul de livrare din gestiune.");
        RuleFor(x => x.ThirdPartyId).NotNull().When(x => x.Kind is WarehouseKind.Custody or WarehouseKind.AtThirdParty)
            .WithMessage("Alegeți partenerul care deține sau păstrează bunurile.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class BlockWarehouseValidator : AbstractValidator<BlockWarehouse>
{
    public BlockWarehouseValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Motivul blocării este obligatoriu.").MaximumLength(250)
            .WithMessage("Motivul are cel mult 250 de caractere.");
    }
}

internal sealed class GetWarehousesHandler(IWarehouseStore store, ICurrentContext context) : IQueryHandler<GetWarehouses, WarehousePageDto>
{
    public async Task<WarehousePageDto> Handle(GetWarehouses request, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null
            : request.Search.Trim().Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal)
                .Replace("_", "[_]", StringComparison.Ordinal);
        var rows = await store.ListAsync(context.CompanyId, search, request.Kind, request.LocationId, request.IncludeInactive, cancellationToken)
            .ConfigureAwait(false);
        var lookups = await store.GetLookupsAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        return new WarehousePageDto(rows.Where(w => WarehouseAccess.CanSee(context, w.DimensionValueId)).ToList(), lookups);
    }
}

internal sealed class GetWarehouseHandler(IWarehouseStore store, ICurrentContext context) : IQueryHandler<GetWarehouse, WarehouseDetailDto>
{
    public async Task<WarehouseDetailDto> Handle(GetWarehouse request, CancellationToken cancellationToken)
    {
        var detail = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false);
        return detail is not null && WarehouseAccess.CanSee(context, detail.DimensionValueId) ? detail : throw WarehouseAccess.NotFound();
    }
}

internal sealed class GetWarehouseHistoryHandler(IWarehouseStore store, ICurrentContext context)
    : IQueryHandler<GetWarehouseHistory, IReadOnlyList<InventoryHistoryEntryDto>>
{
    public Task<IReadOnlyList<InventoryHistoryEntryDto>> Handle(GetWarehouseHistory request, CancellationToken cancellationToken) =>
        store.HistoryAsync(context.CompanyId, request.Id, cancellationToken);
}

internal sealed class SaveWarehouseHandler(IWarehouseStore store, IStockCompanySetup setup, IDimensionLookup dimensions, IOutbox outbox,
    ICurrentContext context, IAuditScope audit) : ICommandHandler<SaveWarehouse, InventorySaved>
{
    public async Task<InventorySaved> Handle(SaveWarehouse request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim();
        var name = request.Name.Trim();
        await setup.EnsureAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        int? dimensionValueId = null;
        if (request.Id is null)
        {
            // S1-A4: valoarea de dimensiune si gestiunea se scriu in aceeasi tranzactie (un refuz din procedura nu lasa valoarea orfana).
            dimensionValueId = await dimensions.EnsureAsync(context.CompanyId, DimensionTypes.Warehouse, code, name, cancellationToken).ConfigureAwait(false);
        }
        else if (await store.GetAsync(context.CompanyId, request.Id.Value, cancellationToken).ConfigureAwait(false) is not { } current
            || !WarehouseAccess.CanSee(context, current.DimensionValueId))
        {
            throw WarehouseAccess.NotFound();
        }

        var record = new WarehouseRecord(request.Id, context.CompanyId, code, name, request.LocationId, request.Kind, request.ValuationBasis,
            request.ValuationBasis == ValuationBasis.RetailPrice ? request.RetailPriceListId : null, request.ThirdPartyId, request.NegativeStockPolicy,
            dimensionValueId, request.DefaultCostCenterId, request.DeliveryOrderMode, request.ReturnWarehouseId, request.Purpose, request.RowVersion);
        var written = await store.SaveAsync(record, context.UserId, cancellationToken).ConfigureAwait(false);
        if (request.Id is not null)
        {
            await dimensions.SyncAsync(written.DimensionValueId, code, name, written.IsActive, cancellationToken).ConfigureAwait(false);
        }

        await WarehouseEvents.PublishAsync(store, outbox, context.CompanyId, written.Id, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Warehouse", written.Id, new
        {
            record.Code, record.Name, record.LocationId, record.Kind, record.ValuationBasis, record.RetailPriceListId, record.ThirdPartyId,
            record.NegativeStockPolicy, record.DefaultCostCenterId, record.DeliveryOrderMode, record.ReturnWarehouseId, record.Purpose,
        });
        return new InventorySaved(written.Id, written.RowVersion);
    }
}

internal sealed class BlockWarehouseHandler(IWarehouseStore store, IOutbox outbox, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<BlockWarehouse, InventorySaved>, ICommandHandler<UnblockWarehouse, InventorySaved>
{
    public Task<InventorySaved> Handle(BlockWarehouse request, CancellationToken cancellationToken) =>
        SetAsync(request.Id, true, request.Reason.Trim(), request.RowVersion, cancellationToken);

    public Task<InventorySaved> Handle(UnblockWarehouse request, CancellationToken cancellationToken) =>
        SetAsync(request.Id, false, null, request.RowVersion, cancellationToken);

    private async Task<InventorySaved> SetAsync(int id, bool blocked, string? reason, byte[] rowVersion, CancellationToken ct)
    {
        await WarehouseAccess.EnsureVisibleAsync(store, context, id, ct).ConfigureAwait(false);
        var written = await store.SetBlockedAsync(context.CompanyId, id, blocked, reason, rowVersion, context.UserId, ct).ConfigureAwait(false);
        await WarehouseEvents.PublishAsync(store, outbox, context.CompanyId, id, ct).ConfigureAwait(false);
        audit.Describe("Inv.Warehouse", id, new { IsBlocked = blocked, Reason = reason });
        return new InventorySaved(id, written.RowVersion);
    }
}

internal sealed class SetWarehouseActiveHandler(IWarehouseStore store, IDimensionLookup dimensions, IOutbox outbox, ICurrentContext context,
    IAuditScope audit) : ICommandHandler<DeactivateWarehouse, InventorySaved>, ICommandHandler<ReactivateWarehouse, InventorySaved>
{
    public Task<InventorySaved> Handle(DeactivateWarehouse request, CancellationToken cancellationToken) =>
        SetAsync(request.Id, false, request.RowVersion, cancellationToken);

    public Task<InventorySaved> Handle(ReactivateWarehouse request, CancellationToken cancellationToken) =>
        SetAsync(request.Id, true, request.RowVersion, cancellationToken);

    private async Task<InventorySaved> SetAsync(int id, bool active, byte[] rowVersion, CancellationToken ct)
    {
        var current = await WarehouseAccess.EnsureVisibleAsync(store, context, id, ct).ConfigureAwait(false);
        var written = await store.SetActiveAsync(context.CompanyId, id, active, rowVersion, context.UserId, ct).ConfigureAwait(false);
        // S1 4.4: valoarea de dimensiune urmeaza starea gestiunii (prin Nucleu).
        await dimensions.SyncAsync(written.DimensionValueId, current.Code, current.Name, active, ct).ConfigureAwait(false);
        await WarehouseEvents.PublishAsync(store, outbox, context.CompanyId, id, ct).ConfigureAwait(false);
        audit.Describe("Inv.Warehouse", id, new { IsActive = active });
        return new InventorySaved(id, written.RowVersion);
    }
}

/// <summary>Accesul pe gestiuni (S1 3.7): gestiunea e valoare a dimensiunii „gestiune”, deci scope-ul rolului o poate exclude.</summary>
internal static class WarehouseAccess
{
    public static bool CanSee(ICurrentContext context, int dimensionValueId) =>
        context.HasPermission(InventoryPermissions.WarehouseView, new DimensionRef(DimensionTypes.Warehouse, dimensionValueId));

    public static ErpException NotFound() => ErpException.NotFound(InventoryErrorCodes.WarehouseNotFound, "Gestiunea nu există în firma curentă.");

    public static async Task<WarehouseDetailDto> EnsureVisibleAsync(IWarehouseStore store, ICurrentContext context, int id, CancellationToken ct) =>
        await store.GetAsync(context.CompanyId, id, ct).ConfigureAwait(false) is { } detail && CanSee(context, detail.DimensionValueId)
            ? detail : throw NotFound();
}

internal static class WarehouseEvents
{
    /// <summary>GestiuneModificata v1 (S1 5.1), cu starea scrisa in tranzactia comenzii.</summary>
    public static async Task PublishAsync(IWarehouseStore store, IOutbox outbox, int companyId, int id, CancellationToken ct)
    {
        var w = await store.GetAsync(companyId, id, ct).ConfigureAwait(false) ?? throw WarehouseAccess.NotFound();
        await outbox.EnqueueAsync(new GestiuneModificata(w.Id, w.Code, w.Name, w.Kind, w.ValuationBasis, w.LocationId, w.IsBlocked, w.IsActive)
        {
            CompanyId = companyId,
        }, ct).ConfigureAwait(false);
    }
}
