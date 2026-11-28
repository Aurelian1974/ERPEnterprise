using Erp.Core.Contracts;
using Erp.Inventory.Contracts;
using FluentValidation;

namespace Erp.Inventory.Application.Warehouses;

/// <summary>Un depozit in ecranul Depozite (macheta s1-05).</summary>
public sealed record DepotItemDto(int Id, string Code, string Name, int LocationId, string LocationName, int? DefaultWarehouseId, int DimensionValueId,
    bool IsBlocked, string? BlockReason, bool IsActive, IReadOnlyList<WarehouseOptionDto> Warehouses, byte[] RowVersion);

/// <summary>O regula de propunere a gestiunii intr-un depozit: pe grupa sau pe articol.</summary>
public sealed record DepotRuleDto(int Id, int DepotId, int? ItemGroupId, string? ItemGroupName, int? ItemId, string? ItemName, int WarehouseId,
    string WarehouseCode);

/// <summary>Suprascrierea nivelului de evidenta pe punct de lucru sau pe locatie.</summary>
public sealed record StockLevelOverrideDto(int Id, int? BranchId, string? BranchName, int? LocationId, string? LocationName, StockKeepingLevel Level);

/// <summary>O locatie activa cu nivelul efectiv si sursa lui (1 firma, 2 punct de lucru, 3 locatie).</summary>
public sealed record LocationLevelDto(int Id, string Code, string Name, int? BranchId, StockKeepingLevel Level, byte Source);

/// <summary>Ecranul Depozite: nivelul de evidenta, depozitele cu gestiunile lor si regulile de propunere.</summary>
public sealed record DepotPageDto(StockKeepingLevel CompanyLevel, IReadOnlyList<StockLevelOverrideDto> Overrides, IReadOnlyList<LocationLevelDto> Locations,
    IReadOnlyList<DepotItemDto> Depots, IReadOnlyList<DepotRuleDto> Rules);

public sealed record DepotRecord(int? Id, int CompanyId, string Code, string Name, int LocationId, int? DefaultWarehouseId, int? DimensionValueId,
    bool IsActive, byte[]? RowVersion);

/// <summary>Accesul la date pentru depozite si nivelul de evidenta (inv.usp_Depot_*, inv.usp_StockLevel_*).</summary>
public interface IDepotStore
{
    Task<DepotPageDto> GetPageAsync(int companyId, CancellationToken ct);

    Task<WarehouseWritten> SaveAsync(DepotRecord depot, int userId, CancellationToken ct);

    Task<WarehouseWritten> SetBlockedAsync(int companyId, int id, bool isBlocked, string? reason, byte[] rowVersion, int userId, CancellationToken ct);

    Task SetWarehouseAsync(int companyId, int depotId, int warehouseId, bool isActive, int userId, CancellationToken ct);

    Task SaveRuleAsync(int companyId, int depotId, int? itemGroupId, int? itemId, int? warehouseId, int userId, CancellationToken ct);

    Task SetLevelAsync(int companyId, int? branchId, int? locationId, StockKeepingLevel? level, int userId, CancellationToken ct);
}

/// <summary>Ecranul Depozite (S1 3.8; macheta s1-05).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseView)]
public sealed record GetDepots : IQuery<DepotPageDto>;

/// <summary>Creeaza sau modifica un depozit; la creare, valoarea de dimensiune „depozit”, in aceeasi tranzactie.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseManage)]
[Audited("Inv.Depot.Save")]
public sealed record SaveDepot(int? Id, string Code, string Name, int LocationId, int? DefaultWarehouseId, bool IsActive, byte[]? RowVersion)
    : ICommand<InventorySaved>;

/// <summary>Blocheaza sau deblocheaza depozitul (blocheaza toate gestiunile din el).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseBlock)]
[Audited("Inv.Depot.Block")]
public sealed record SetDepotBlocked(int Id, bool IsBlocked, string? Reason, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Pune sau scoate o gestiune dintr-un depozit.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseManage)]
[Audited("Inv.DepotWarehouse.Save")]
public sealed record SetDepotWarehouse(int DepotId, int WarehouseId, bool IsActive) : ICommand<bool>;

/// <summary>Regula de propunere a gestiunii: pe grupa sau pe articol; WarehouseId null sterge regula.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseManage)]
[Audited("Inv.DepotWarehouseRule.Save")]
public sealed record SaveDepotRule(int DepotId, int? ItemGroupId, int? ItemId, int? WarehouseId) : ICommand<bool>;

/// <summary>Nivelul de evidenta pe firma, punct de lucru sau locatie (S1 3.8); Level null sterge suprascrierea.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.SettingsManage)]
[Audited("Inv.StockLevel.Set")]
public sealed record SetStockLevel(int? BranchId, int? LocationId, StockKeepingLevel? Level) : ICommand<bool>;

internal sealed class SaveDepotValidator : AbstractValidator<SaveDepot>
{
    public SaveDepotValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Codul este obligatoriu.")
            .Matches("^[A-Za-z0-9][A-Za-z0-9._/-]{0,19}$").WithMessage("Codul are 1–20 de caractere: litere, cifre, „.”, „_”, „/” sau „-”.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Denumirea este obligatorie.").MaximumLength(200).WithMessage("Denumirea are cel mult 200 de caractere.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class SetDepotBlockedValidator : AbstractValidator<SetDepotBlocked>
{
    public SetDepotBlockedValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().When(x => x.IsBlocked).WithMessage("Motivul blocării este obligatoriu.").MaximumLength(250)
            .WithMessage("Motivul are cel mult 250 de caractere.");
    }
}

internal sealed class SetStockLevelValidator : AbstractValidator<SetStockLevel>
{
    public SetStockLevelValidator()
    {
        RuleFor(x => x.Level).IsInEnum().When(x => x.Level is not null).WithMessage("Alegeți nivelul de evidență.");
        RuleFor(x => x.Level).NotNull().When(x => x.BranchId is null && x.LocationId is null).WithMessage("Firma are mereu un nivel de evidență.");
    }
}

internal sealed class GetDepotsHandler(IDepotStore store, IStockCompanySetup setup, ICurrentContext context) : IQueryHandler<GetDepots, DepotPageDto>
{
    public async Task<DepotPageDto> Handle(GetDepots request, CancellationToken cancellationToken)
    {
        await setup.EnsureAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        return await store.GetPageAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class DepotHandler(IDepotStore store, IStockCompanySetup setup, IDimensionLookup dimensions, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveDepot, InventorySaved>, ICommandHandler<SetDepotBlocked, InventorySaved>, ICommandHandler<SetDepotWarehouse, bool>,
    ICommandHandler<SaveDepotRule, bool>, ICommandHandler<SetStockLevel, bool>
{
    public async Task<InventorySaved> Handle(SaveDepot request, CancellationToken cancellationToken)
    {
        await setup.EnsureAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        var code = request.Code.Trim();
        var name = request.Name.Trim();
        int? dimension = request.Id is null
            ? await dimensions.EnsureAsync(context.CompanyId, DimensionTypes.Depot, code, name, cancellationToken).ConfigureAwait(false)
            : null;
        var written = await store.SaveAsync(new DepotRecord(request.Id, context.CompanyId, code, name, request.LocationId, request.DefaultWarehouseId,
            dimension, request.IsActive, request.RowVersion), context.UserId, cancellationToken).ConfigureAwait(false);
        if (request.Id is not null)
        {
            await dimensions.SyncAsync(written.DimensionValueId, code, name, written.IsActive, cancellationToken).ConfigureAwait(false);
        }

        audit.Describe("Inv.Depot", written.Id, new { Code = code, Name = name, request.LocationId, request.DefaultWarehouseId, request.IsActive });
        return new InventorySaved(written.Id, written.RowVersion);
    }

    public async Task<InventorySaved> Handle(SetDepotBlocked request, CancellationToken cancellationToken)
    {
        var written = await store.SetBlockedAsync(context.CompanyId, request.Id, request.IsBlocked, request.IsBlocked ? request.Reason?.Trim() : null,
            request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Depot", request.Id, new { request.IsBlocked, request.Reason });
        return new InventorySaved(written.Id, written.RowVersion);
    }

    public async Task<bool> Handle(SetDepotWarehouse request, CancellationToken cancellationToken)
    {
        await store.SetWarehouseAsync(context.CompanyId, request.DepotId, request.WarehouseId, request.IsActive, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Inv.Depot", request.DepotId, new { request.WarehouseId, request.IsActive });
        return true;
    }

    public async Task<bool> Handle(SaveDepotRule request, CancellationToken cancellationToken)
    {
        await store.SaveRuleAsync(context.CompanyId, request.DepotId, request.ItemGroupId, request.ItemId, request.WarehouseId, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Depot", request.DepotId, new { Rule = new { request.ItemGroupId, request.ItemId, request.WarehouseId } });
        return true;
    }

    public async Task<bool> Handle(SetStockLevel request, CancellationToken cancellationToken)
    {
        await setup.EnsureAsync(context.CompanyId, cancellationToken).ConfigureAwait(false);
        await store.SetLevelAsync(context.CompanyId, request.BranchId, request.LocationId, request.Level, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Inv.StockLevel", request.LocationId ?? request.BranchId, new { request.BranchId, request.LocationId, request.Level });
        return true;
    }
}
