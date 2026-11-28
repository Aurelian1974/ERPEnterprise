using Erp.Core.Contracts;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using FluentValidation;

namespace Erp.Inventory.Application.Documents;

/// <summary>
/// Notele de predare si de primire (02.05, S2 3.4, 4.2–4.3): etapele deduse din locatii, miscarile perechi la acelasi cost, expeditia in tranzit,
/// primirea cu diferentele si rezolvarea lor (retur, lipsa imputabila sau nu, plus), starea expeditiei si evenimentele TransferExpediat,
/// TransferPrimit.
/// </summary>
public sealed class TransferService(IStockDocumentStore store, StockLedger ledger, INumberSeriesService numbers, ICurrentContext context, IOutbox outbox,
    IAuditScope audit)
{
    /// <summary>Sursa miscarilor de rezolvare a diferentelor, separata de miscarile notei de primire (aceeasi linie sursa).</summary>
    public const string DifferenceSource = "inv-diff";

    internal async Task<InventorySaved> ValidateTransferAsync(StockDocumentDto document, StockDocumentKind kind, byte[] rowVersion, CancellationToken ct)
    {
        await store.BeginValidationAsync(context.CompanyId, document.Id, rowVersion, ct).ConfigureAwait(false);
        var plan = await store.PlanTransferAsync(context.CompanyId, document.Id, ct).ConfigureAwait(false);
        // O etapa: cel care preda are drept pe ambele gestiuni; doua etape: doar pe cea care preda (S2 3.4).
        DocumentAccess.Require(context, kind.ValidatePermission, plan.TransferMode == 1 ? document.WarehouseDimensions : document.WarehouseDimensions.Take(1));
        var number = await numbers.NextAsync(context.CompanyId, kind.NumberSeriesType, document.DocumentDate, ct).ConfigureAwait(false);
        var lines = new List<StockMovementRequest>();
        foreach (var l in document.Lines)
        {
            var qty = Quantity.ParseOptional(l.QtyBase) ?? 0;
            lines.Add(new StockMovementRequest(l.Id, 1, "TRANSFER_OUT", l.FromDepotId ?? document.FromDepotId, l.FromWarehouseId ?? document.FromWarehouseId ?? 0,
                l.ItemVariantId, -qty, null, null, null, null, null, l.CostCenterId ?? document.CostCenterId, l.ProjectId ?? document.ProjectId, null));
            lines.Add(plan.TransferMode == 1
                ? new StockMovementRequest(l.Id, 2, "TRANSFER_IN", l.ToDepotId ?? document.ToDepotId, l.ToWarehouseId ?? document.ToWarehouseId ?? 0,
                    l.ItemVariantId, qty, null, null, null, null, null, l.CostCenterId ?? document.CostCenterId, l.ProjectId ?? document.ProjectId, null)
                : new StockMovementRequest(l.Id, 2, "TRANSFER_IN", null, plan.TransitWarehouseId, l.ItemVariantId, qty, null, null, null, null, null,
                    l.CostCenterId ?? document.CostCenterId, l.ProjectId ?? document.ProjectId, null));
        }

        var outcome = await ledger.PostWithOutcomeAsync(new StockPostingRequest(context.CompanyId, "inv", document.DocumentType, document.Id, number.Formatted,
            document.DocumentDate, document.AccountingDate, document.PartnerId, false, lines), document.EmployeeId, ct).ConfigureAwait(false);
        var shipment = await store.MarkShippedAsync(context.CompanyId, document.Id, plan.TransferMode, context.UserId, ct).ConfigureAwait(false);
        var saved = await store.MarkValidatedAsync(context.CompanyId, document.Id, number.Formatted, context.UserId, ct).ConfigureAwait(false);
        if (shipment is { } shipmentId)
        {
            await outbox.EnqueueAsync(new TransferExpediat(shipmentId, document.Id, document.FromWarehouseId ?? 0, document.ToWarehouseId ?? 0,
                outcome.Movements.Where(m => m.SourceRole == 1).Select(m => new StockMovementEventLine(m.MovementId, m.WarehouseId, m.DepotId, m.ItemVariantId,
                    m.QtyBase, m.UnitCost, m.CostAmount, m.MovementTypeCode, null, m.IsForcedNegative)).ToList()) { CompanyId = context.CompanyId }, ct)
                .ConfigureAwait(false);
        }

        audit.Describe("Inv.StockDocument", document.Id, new { Number = number.Formatted, plan.TransferMode, Shipment = shipment });
        return saved;
    }

    internal async Task<InventorySaved> ValidateReceiptAsync(StockDocumentDto document, StockDocumentKind kind, byte[] rowVersion, CancellationToken ct)
    {
        // Primirea o valideaza cine are drept pe gestiunea care primeste (S2 3.4).
        DocumentAccess.Require(context, kind.ValidatePermission, document.WarehouseDimensions.Skip(1));
        await store.BeginValidationAsync(context.CompanyId, document.Id, rowVersion, ct).ConfigureAwait(false);
        var shipped = await store.GetAsync(context.CompanyId, document.RelatedDocumentId ?? 0, ct).ConfigureAwait(false) ?? throw DocumentAccess.NotFound();
        var number = await numbers.NextAsync(context.CompanyId, kind.NumberSeriesType, document.DocumentDate, ct).ConfigureAwait(false);
        var transit = document.FromWarehouseId ?? 0;
        var received = new List<StockMovementRequest>();
        var differences = new List<StockMovementRequest>();
        var reasons = await store.ReasonsAsync(context.CompanyId, ct).ConfigureAwait(false);
        int Reason(string code) => reasons.First(r => r.Code == code).Id;
        foreach (var l in document.Lines)
        {
            var sent = Quantity.ParseOptional(l.QtyBase) ?? 0;
            var got = Quantity.ParseOptional(l.ReceivedQtyBase) ?? sent;
            var difference = sent - got;
            var toWarehouse = l.ToWarehouseId ?? document.ToWarehouseId ?? 0;
            var toDepot = l.ToDepotId ?? document.ToDepotId;
            var intoStock = Math.Min(got, sent);
            if (intoStock > 0)
            {
                received.Add(new StockMovementRequest(l.Id, 1, "TRANSFER_OUT", null, transit, l.ItemVariantId, -intoStock, null, null, null, null, null,
                    l.CostCenterId, l.ProjectId, null));
                received.Add(new StockMovementRequest(l.Id, 2, "TRANSFER_IN", toDepot, toWarehouse, l.ItemVariantId, intoStock, null, null, null, null, null,
                    l.CostCenterId, l.ProjectId, null));
            }

            switch (l.DifferenceResolution)
            {
                case 2 when difference > 0:
                    // Retur la expeditor: din tranzit inapoi in gestiunea care a predat, la acelasi cost.
                    differences.Add(new StockMovementRequest(l.Id, 1, "TRANSFER_OUT", null, transit, l.ItemVariantId, -difference, null, null, null, null, null,
                        l.CostCenterId, l.ProjectId, null));
                    differences.Add(new StockMovementRequest(l.Id, 2, "TRANSFER_IN", shipped.FromDepotId, shipped.FromWarehouseId ?? 0, l.ItemVariantId,
                        difference, null, null, null, null, null, l.CostCenterId, l.ProjectId, null));
                    break;
                case 3 or 4 when difference > 0:
                    differences.Add(new StockMovementRequest(l.Id, 1, "UNLOAD", null, transit, l.ItemVariantId, -difference, null, null, null, null, null,
                        l.CostCenterId, l.ProjectId, Reason(l.DifferenceResolution == 3 ? "SHORTAGE_CHARGED" : "SHORTAGE_TRANSIT")));
                    break;
                case 5 when difference < 0:
                    // Plus la primire: incarcare in gestiunea care primeste, la costul liniei predate.
                    differences.Add(new StockMovementRequest(l.Id, 1, "LOAD", toDepot, toWarehouse, l.ItemVariantId, -difference,
                        Quantity.ParseOptional(l.UnitCost), null, null, null, null, l.CostCenterId, l.ProjectId, Reason("TRANSFER_SURPLUS")));
                    break;
            }
        }

        await ledger.PostWithOutcomeAsync(new StockPostingRequest(context.CompanyId, "inv", document.DocumentType, document.Id, number.Formatted,
            document.DocumentDate, document.AccountingDate, null, false, received), null, ct).ConfigureAwait(false);
        await ledger.PostWithOutcomeAsync(new StockPostingRequest(context.CompanyId, DifferenceSource, document.DocumentType, document.Id, number.Formatted,
            document.DocumentDate, document.AccountingDate, null, false, differences), document.EmployeeId, ct).ConfigureAwait(false);
        var saved = await store.MarkValidatedAsync(context.CompanyId, document.Id, number.Formatted, context.UserId, ct).ConfigureAwait(false);
        var status = await store.RefreshShipmentAsync(context.CompanyId, document.TransitShipmentId ?? 0, context.UserId, ct).ConfigureAwait(false);
        await outbox.EnqueueAsync(new TransferPrimit(document.TransitShipmentId ?? 0, document.Id, status, document.Lines.Select(l =>
            new TransferReceivedLine(l.ItemVariantId, Quantity.ParseOptional(l.QtyBase) ?? 0, Quantity.ParseOptional(l.ReceivedQtyBase) ?? 0,
                Quantity.ParseOptional(l.DifferenceQtyBase) ?? 0, l.DifferenceResolution)).ToList()) { CompanyId = context.CompanyId }, ct).ConfigureAwait(false);
        audit.Describe("Inv.StockDocument", document.Id, new { Number = number.Formatted, ShipmentStatus = status });
        return saved;
    }
}

internal sealed class SaveReceiptValidator : AbstractValidator<SaveReceipt>
{
    public SaveReceiptValidator()
    {
        RuleForEach(x => x.Lines).Must(l => Quantity.TryParse(l.ReceivedQtyBase, out var q) && q >= 0)
            .WithMessage("Cantitatea primită este pozitivă sau zero, cu cel mult șase zecimale.");
        RuleForEach(x => x.Lines).Must(l => l.Resolution is null or >= 1 and <= 5).WithMessage("Alegeți rezolvarea diferenței.");
    }
}

internal sealed class ReceiptHandler(IStockDocumentStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<CreateReceiptFromShipment, InventorySaved>, ICommandHandler<SaveReceipt, InventorySaved>,
    IQueryHandler<GetTransitShipments, IReadOnlyList<TransitShipmentDto>>
{
    public async Task<IReadOnlyList<TransitShipmentDto>> Handle(GetTransitShipments request, CancellationToken cancellationToken) =>
        (await store.ShipmentsAsync(context.CompanyId, request.ToWarehouseId, request.OpenOnly, cancellationToken).ConfigureAwait(false))
        .Where(s => s.WarehouseDimensions.Any(d => context.HasPermission(InventoryPermissions.DocumentView, new DimensionRef(DimensionTypes.Warehouse, d))))
        .ToList();

    public async Task<InventorySaved> Handle(CreateReceiptFromShipment request, CancellationToken cancellationToken)
    {
        var shipment = (await store.ShipmentsAsync(context.CompanyId, null, false, cancellationToken).ConfigureAwait(false)).FirstOrDefault(s => s.Id == request.ShipmentId)
            ?? throw DocumentAccess.NotFound();
        DocumentAccess.Require(context, InventoryPermissions.TransferEdit, shipment.WarehouseDimensions.Skip(1));
        var id = await store.CreateReceiptAsync(context.CompanyId, request.ShipmentId, request.DocumentDate, context.UserId, cancellationToken).ConfigureAwait(false);
        var document = await store.GetAsync(context.CompanyId, id, cancellationToken).ConfigureAwait(false) ?? throw DocumentAccess.NotFound();
        audit.Describe("Inv.StockDocument", id, new { Shipment = request.ShipmentId, request.DocumentDate });
        return new InventorySaved(id, document.RowVersion);
    }

    public async Task<InventorySaved> Handle(SaveReceipt request, CancellationToken cancellationToken)
    {
        var document = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false) is { } d
            && DocumentAccess.CanSee(context, d.WarehouseDimensions) ? d : throw DocumentAccess.NotFound();
        DocumentAccess.Require(context, InventoryPermissions.TransferEdit, document.WarehouseDimensions.Skip(1));
        var saved = await store.SaveReceiptAsync(context.CompanyId, request.Id, string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            request.Lines.Select(l => new ReceiptLineRecord(l.LineId, Quantity.ParseOptional(l.ReceivedQtyBase) ?? 0, l.Resolution)).ToList(), request.RowVersion,
            context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.StockDocument", request.Id, new { Received = request.Lines.Select(l => new { l.LineId, l.ReceivedQtyBase, l.Resolution }) });
        return saved;
    }
}
