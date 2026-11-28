using Erp.Core.Contracts;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Erp.Inventory.Domain.Lots;
using FluentValidation;

namespace Erp.Inventory.Application.Lots;

/// <summary>Un lot cu stoc intr-o gestiune (machetele s4-01, s4-02).</summary>
public sealed record LotItemDto(int LotId, int ItemVariantId, string VariantCode, string VariantName, string LotCode, string CkuCode, string? SupplierLotCode,
    DateOnly? ExpiryDate, byte? ExpiryKind, int? DaysLeft, LotStatus Status, bool IsTechnical, int WarehouseId, string WarehouseCode, string QtyOnHand)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int WarehouseDimensionId { get; init; }
}

public sealed record LotHistoryDto(LotStatus? FromStatus, LotStatus ToStatus, string? Reason, string? SourceDocumentType, long? SourceDocumentId, DateTime ChangedAt,
    string? ChangedByName);

public sealed record LotStockDto(int WarehouseId, string WarehouseCode, string QtyOnHand);

/// <summary>Fisa lotului (S4 3.2; macheta s4-01): atributele, starea, istoricul, stocul pe gestiuni.</summary>
public sealed record LotDto(int LotId, int ItemVariantId, string VariantCode, string VariantName, string LotCode, string CkuCode, Guid CkuUid,
    string? SupplierLotCode, int? SupplierPartnerId, string? SupplierName, DateOnly? ProductionDate, DateOnly? ExpiryDate, byte? ExpiryKind,
    string? CountryOfOriginCode, LotStatus Status, string? StatusReason, bool IsTechnical, DateOnly? FirstReceiptDate, IReadOnlyList<LotHistoryDto> History,
    IReadOnlyList<LotStockDto> Stock, byte[] RowVersion);

/// <summary>Rezultatul unei schimbari de stare (inv.usp_Lot_ChangeStatus), pentru eveniment.</summary>
public sealed record LotStatusChange(int LotId, int ItemVariantId, string LotCode, LotStatus FromStatus, LotStatus ToStatus, int? SupplierPartnerId,
    byte[] RowVersion);

public sealed record LotExpiryScan(IReadOnlyList<(int LotId, int ItemVariantId, string LotCode, LotStatus FromStatus, int? SupplierPartnerId)> Expired,
    IReadOnlyList<(int WarehouseId, LotExpiryLine Line)> Warnings);

public sealed record PartnerShelfLifeRuleDto(int Id, int PartnerId, string PartnerName, int? ItemId, string? ItemName, int? ItemGroupId, string? ItemGroupName,
    int MinRemainingDays);

public sealed record LotLookup(int? LotId = null, string? CkuCode = null, Guid? CkuUid = null, int? ItemVariantId = null, string? LotCode = null,
    DateOnly? ExpiryDate = null);

/// <summary>Accesul la date pentru loturi (inv.usp_Lot_*, inv.usp_PartnerShelfLifeRule_Save).</summary>
public interface ILotStore
{
    Task<IReadOnlyList<LotItemDto>> ListAsync(int companyId, int? itemVariantId, int? warehouseId, string? search, int? expiringWithinDays, DateOnly today,
        CancellationToken ct);

    Task<LotDto?> GetAsync(int companyId, LotLookup lookup, CancellationToken ct);

    Task<LotStatusChange> ChangeStatusAsync(int companyId, int lotId, LotStatus to, string? reason, byte[]? rowVersion, string? documentType, long? documentId,
        bool isSystem, int userId, CancellationToken ct);

    Task<InventorySaved> UpdateAsync(int companyId, int lotId, string? supplierLotCode, string? countryCode, DateOnly? productionDate, DateOnly? expiryDate,
        string reason, byte[] rowVersion, int userId, CancellationToken ct);

    Task<LotExpiryScan> ExpiryScanAsync(int companyId, DateOnly today, int userId, CancellationToken ct);

    Task<(IReadOnlyList<LotCandidate> Lots, int MinRemainingDays, byte PickRule)> ProposeAsync(int companyId, int warehouseId, int? depotId, int itemVariantId,
        int? partnerId, DateOnly onDate, CancellationToken ct);

    Task<IReadOnlyList<PartnerShelfLifeRuleDto>> SaveShelfLifeRuleAsync(int companyId, int partnerId, int? itemId, int? itemGroupId, int? minRemainingDays,
        int userId, CancellationToken ct);
}

/// <summary>
/// Loturile (02.05, S4 3.2, 3.3, 3.5): starea (cu istoric si LotStareSchimbata), atributele, propunerea FEFO cu aceeasi functie ca la validare
/// (LotAllocator), jobul de expirare.
/// </summary>
public sealed class LotService(ILotStore store, IOutbox outbox, ICurrentContext context)
{
    public async Task<LotStatusChange> ChangeStatusAsync(int companyId, int lotId, LotStatus to, string? reason, byte[]? rowVersion, string? documentType,
        long? documentId, bool isSystem, CancellationToken ct)
    {
        var change = await store.ChangeStatusAsync(companyId, lotId, to, reason, rowVersion, documentType, documentId, isSystem, context.UserId, ct)
            .ConfigureAwait(false);
        await outbox.EnqueueAsync(new LotStareSchimbata(change.LotId, change.ItemVariantId, change.LotCode, change.FromStatus, change.ToStatus, reason,
            documentType is null ? null : $"{documentType} {documentId}", change.SupplierPartnerId) { CompanyId = companyId }, ct).ConfigureAwait(false);
        return change;
    }

    public async Task<IReadOnlyList<LotAllocationDto>> ProposeAsync(LotProposalRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (lots, minDays, pickRule) = await store.ProposeAsync(request.CompanyId, request.WarehouseId, request.DepotId, request.ItemVariantId,
            request.CustomerPartnerId, request.DeliveryDate, ct).ConfigureAwait(false);
        var type = request.CustomerPartnerId is null ? "UNLOAD" : "ISSUE_SALE";
        var demand = new LotDemand(1, request.WarehouseId, request.DepotId, request.ItemVariantId, request.QtyBase, null, type, minDays);
        var rights = new LotRights(context.HasPermission(InventoryPermissions.LotSellAfterBestBefore), false);
        var ordered = LotAllocator.Eligible(lots, demand, request.DeliveryDate, pickRule, rights).ToList();
        var left = request.QtyBase;
        var result = new List<LotAllocationDto>();
        foreach (var lot in ordered)
        {
            if (left <= 0)
            {
                break;
            }

            var qty = Math.Min(left, lot.Available);
            left -= qty;
            result.Add(new LotAllocationDto(lot.LotId, lot.CkuCode ?? lot.LotCode, lot.LotCode, lot.ExpiryDate, qty,
                lot.ExpiryDate is { } e ? e.DayNumber - request.DeliveryDate.DayNumber : 0, result.Count == 0));
        }

        return result;
    }

    /// <summary>Jobul de expirare pe o firma (S4 3.2): loturile cu data limita de consum depasita trec in Expirat; avertizarea pe gestiune.</summary>
    public async Task<int> ScanExpiryAsync(int companyId, DateOnly today, CancellationToken ct)
    {
        var scan = await store.ExpiryScanAsync(companyId, today, context.UserId, ct).ConfigureAwait(false);
        foreach (var lot in scan.Expired)
        {
            await outbox.EnqueueAsync(new LotStareSchimbata(lot.LotId, lot.ItemVariantId, lot.LotCode, lot.FromStatus, LotStatus.Expired,
                "Data limită de consum a trecut", null, lot.SupplierPartnerId) { CompanyId = companyId }, ct).ConfigureAwait(false);
        }

        foreach (var warehouse in scan.Warnings.GroupBy(w => w.WarehouseId))
        {
            await outbox.EnqueueAsync(new LoturiAproapeDeExpirare(warehouse.Key, warehouse.Select(w => w.Line).ToList()) { CompanyId = companyId }, ct)
                .ConfigureAwait(false);
        }

        return scan.Expired.Count;
    }
}

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record GetLots(int? ItemVariantId = null, int? WarehouseId = null, string? Search = null, int? ExpiringWithinDays = null)
    : IQuery<IReadOnlyList<LotItemDto>>;

[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record GetLot(int Id) : IQuery<LotDto>;

/// <summary>Propunerea FEFO pentru o linie (macheta s4-03), aceeasi ca la validare.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
public sealed record ProposeLots(int WarehouseId, int? DepotId, int ItemVariantId, string QtyBase, int? CustomerPartnerId, DateOnly DeliveryDate)
    : IQuery<IReadOnlyList<LotAllocationDto>>;

/// <summary>Carantina, eliberarea, blocarea, deblocarea unui lot (S4 3.3): Inv.Lot.Release pentru carantina, Inv.Lot.Block pentru blocare.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotView)]
[Audited("Inv.Lot.ChangeStatus")]
public sealed record ChangeLotStatus(int Id, LotStatus ToStatus, string? Reason, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Atributele lotului (S4 4.4): lotul furnizorului, tara, fabricatia, termenul, cu motiv.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotEdit)]
[Audited("Inv.Lot.Update")]
public sealed record UpdateLot(int Id, string? SupplierLotCode, string? CountryOfOriginCode, DateOnly? ProductionDate, DateOnly? ExpiryDate, string Reason,
    byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Durata minima ramasa ceruta de un client, pe model sau grupa (S4 3.2, decizia 4); fara zile, regula se sterge.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.ItemSettingsManage)]
[Audited("Inv.PartnerShelfLifeRule.Save")]
public sealed record SavePartnerShelfLifeRule(int PartnerId, int? ItemId, int? ItemGroupId, int? MinRemainingDays) : ICommand<IReadOnlyList<PartnerShelfLifeRuleDto>>;

/// <summary>Jobul zilnic de expirare pe firma curenta (S4 3.2); rulat de LotExpiryJob in contextul de sistem.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.LotEdit)]
public sealed record ScanLotExpiry(DateOnly Today) : ICommand<int>;

internal sealed class ChangeLotStatusValidator : AbstractValidator<ChangeLotStatus>
{
    public ChangeLotStatusValidator()
    {
        RuleFor(x => x.ToStatus).Must(s => s is LotStatus.Available or LotStatus.Quarantine or LotStatus.Blocked)
            .WithMessage("Manual, lotul trece doar între Disponibil, Carantină și Blocat.");
        RuleFor(x => x.Reason).NotEmpty().When(x => x.ToStatus != LotStatus.Available).WithMessage("Motivul este obligatoriu.").MaximumLength(400);
    }
}

internal sealed class UpdateLotValidator : AbstractValidator<UpdateLot>
{
    public UpdateLotValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Motivul este obligatoriu.").MaximumLength(400);
        RuleFor(x => x.CountryOfOriginCode).Length(2).When(x => x.CountryOfOriginCode is not null).WithMessage("Țara se dă prin codul ISO din două litere.");
    }
}

internal sealed class LotHandler(ILotStore store, LotService service, ICurrentContext context, TimeProvider time, IAuditScope audit)
    : IQueryHandler<GetLots, IReadOnlyList<LotItemDto>>, IQueryHandler<GetLot, LotDto>, IQueryHandler<ProposeLots, IReadOnlyList<LotAllocationDto>>,
    ICommandHandler<ChangeLotStatus, InventorySaved>, ICommandHandler<UpdateLot, InventorySaved>,
    ICommandHandler<SavePartnerShelfLifeRule, IReadOnlyList<PartnerShelfLifeRuleDto>>, ICommandHandler<ScanLotExpiry, int>
{
    private static ErpException NotFound() => ErpException.NotFound("INV_LOT_NOT_FOUND", "Lotul nu există în firma curentă.");

    public async Task<IReadOnlyList<LotItemDto>> Handle(GetLots request, CancellationToken cancellationToken) =>
        (await store.ListAsync(context.CompanyId, request.ItemVariantId, request.WarehouseId, string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            request.ExpiringWithinDays, BusinessDay.Today(time), cancellationToken).ConfigureAwait(false))
        .Where(l => context.HasPermission(InventoryPermissions.LotView, new DimensionRef(DimensionTypes.Warehouse, l.WarehouseDimensionId)))
        .ToList();

    public async Task<LotDto> Handle(GetLot request, CancellationToken cancellationToken) =>
        await store.GetAsync(context.CompanyId, new LotLookup(request.Id), cancellationToken).ConfigureAwait(false) ?? throw NotFound();

    public Task<IReadOnlyList<LotAllocationDto>> Handle(ProposeLots request, CancellationToken cancellationToken) =>
        service.ProposeAsync(new LotProposalRequest(context.CompanyId, request.WarehouseId, request.DepotId, request.ItemVariantId,
            Quantity.ParseOptional(request.QtyBase) ?? 0, request.CustomerPartnerId, request.DeliveryDate, null), cancellationToken);

    public async Task<InventorySaved> Handle(ChangeLotStatus request, CancellationToken cancellationToken)
    {
        var lot = await store.GetAsync(context.CompanyId, new LotLookup(request.Id), cancellationToken).ConfigureAwait(false) ?? throw NotFound();
        var permission = lot.Status == LotStatus.Quarantine || request.ToStatus == LotStatus.Quarantine ? InventoryPermissions.LotRelease : InventoryPermissions.LotBlock;
        if (!context.HasPermission(permission))
        {
            throw new ErpException(ErpErrorKind.Forbidden, CoreErrorCodes.Forbidden, "Nu aveți dreptul să schimbați această stare a lotului.");
        }

        var change = await service.ChangeStatusAsync(context.CompanyId, request.Id, request.ToStatus, request.Reason?.Trim(), request.RowVersion, null, null, false,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Lot", request.Id, new { From = change.FromStatus, To = change.ToStatus, request.Reason });
        return new InventorySaved(request.Id, change.RowVersion);
    }

    public async Task<InventorySaved> Handle(UpdateLot request, CancellationToken cancellationToken)
    {
        var saved = await store.UpdateAsync(context.CompanyId, request.Id, request.SupplierLotCode, request.CountryOfOriginCode?.ToUpperInvariant(),
            request.ProductionDate, request.ExpiryDate, request.Reason.Trim(), request.RowVersion, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Lot", request.Id, new { request.SupplierLotCode, request.ExpiryDate, request.Reason });
        return saved;
    }

    public async Task<IReadOnlyList<PartnerShelfLifeRuleDto>> Handle(SavePartnerShelfLifeRule request, CancellationToken cancellationToken)
    {
        var rules = await store.SaveShelfLifeRuleAsync(context.CompanyId, request.PartnerId, request.ItemId, request.ItemGroupId, request.MinRemainingDays,
            context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.PartnerShelfLifeRule", request.PartnerId, new { request.ItemId, request.ItemGroupId, request.MinRemainingDays });
        return rules;
    }

    public Task<int> Handle(ScanLotExpiry request, CancellationToken cancellationToken) =>
        service.ScanExpiryAsync(context.CompanyId, request.Today, cancellationToken);
}
