using Erp.Inventory.Application;
using Erp.Inventory.Application.Costing;
using Erp.Inventory.Application.Documents;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Contracts;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Lots;
using Erp.Inventory.Application.Settings;
using Erp.Inventory.Application.Warehouses;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Inventory.Api;

public static class InventoryApiRegistration
{
    /// <summary>Endpoint-urile Stocurilor, subtiri (NET-03): cererea merge direct in MediatR; firma e cea curenta (antetul cererii).</summary>
    public static IEndpointRouteBuilder MapInventoryApi(this IEndpointRouteBuilder endpoints)
    {
        var inventory = endpoints.MapGroup("/api/inventory").WithTags("Inventory");
        MapWarehouses(inventory);
        MapKeepers(inventory);
        MapDepots(inventory);
        MapDocuments(inventory);
        MapLandedCosts(inventory);
        MapRetail(inventory);
        MapValuation(inventory);
        MapLots(inventory);
        MapItemSettings(inventory);
        MapSettings(inventory);
        return endpoints;
    }

    // Stocuri › Gestiuni (02.05, S1 3.1 si 4.1–4.4; macheta s1-01).
    private static void MapWarehouses(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/warehouses", ([AsParameters] GetWarehouses query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetWarehouses").Produces<WarehousePageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        inventory.MapGet("/warehouses/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetWarehouse(id), ct))
            .WithName("GetWarehouse").Produces<WarehouseDetailDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        inventory.MapGet("/warehouses/{id:int}/history", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetWarehouseHistory(id), ct))
            .WithName("GetWarehouseHistory").Produces<IReadOnlyList<InventoryHistoryEntryDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        inventory.MapPost("/warehouses", (SaveWarehouse body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateWarehouse").Saves();
        inventory.MapPut("/warehouses/{id:int}", (int id, SaveWarehouse body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateWarehouse").Saves();
        inventory.MapPost("/warehouses/{id:int}/block", (int id, BlockWarehouse body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("BlockWarehouse").Saves();
        inventory.MapPost("/warehouses/{id:int}/unblock", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new UnblockWarehouse(id, body.RowVersion), ct))
            .WithName("UnblockWarehouse").Saves();
        inventory.MapPost("/warehouses/{id:int}/deactivate", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new DeactivateWarehouse(id, body.RowVersion), ct))
            .WithName("DeactivateWarehouse").Saves();
        inventory.MapPost("/warehouses/{id:int}/reactivate", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ReactivateWarehouse(id, body.RowVersion), ct))
            .WithName("ReactivateWarehouse").Saves();
    }

    // Stocuri › Gestiune › Gestionari si predarea-primirea (02.05, S1 3.2 si 4.2; macheta s1-02).
    private static void MapKeepers(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/warehouses/{id:int}/keepers", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetWarehouseKeepers(id), ct))
            .WithName("GetWarehouseKeepers").Produces<WarehouseKeepersDto>()
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound);
        inventory.MapPost("/warehouses/{id:int}/keepers", (int id, SaveWarehouseKeeper body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, WarehouseId = id }, ct))
            .WithName("CreateWarehouseKeeper").Saves();
        inventory.MapPut("/warehouses/{id:int}/keepers/{keeperId:int}", (int id, int keeperId, SaveWarehouseKeeper body, ISender sender,
                CancellationToken ct) => sender.Send(body with { Id = keeperId, WarehouseId = id }, ct))
            .WithName("UpdateWarehouseKeeper").Saves();
        inventory.MapPost("/warehouses/{id:int}/handovers", (int id, StartHandover body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { WarehouseId = id }, ct))
            .WithName("StartHandover").Saves();
        inventory.MapPost("/handovers/{id:int}/complete", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new CompleteHandover(id, body.RowVersion), ct))
            .WithName("CompleteHandover").Saves();
        inventory.MapPost("/handovers/{id:int}/cancel", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new CancelHandover(id, body.RowVersion), ct))
            .WithName("CancelHandover").Saves();
    }

    // Stocuri › Depozite si nivelul de evidenta (02.05, S1 3.8; macheta s1-05).
    private static void MapDepots(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/depots", (ISender sender, CancellationToken ct) => sender.Send(new GetDepots(), ct))
            .WithName("GetDepots").Produces<DepotPageDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        inventory.MapPost("/depots", (SaveDepot body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateDepot").Saves();
        inventory.MapPut("/depots/{id:int}", (int id, SaveDepot body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateDepot").Saves();
        inventory.MapPost("/depots/{id:int}/blocked", (int id, SetDepotBlocked body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("SetDepotBlocked").Saves();
        inventory.MapPost("/depots/{id:int}/warehouses", (int id, SetDepotWarehouse body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { DepotId = id }, ct))
            .WithName("SetDepotWarehouse").Produces<bool>().ProducesProblem(StatusCodes.Status409Conflict);
        inventory.MapPost("/depots/{id:int}/rules", (int id, SaveDepotRule body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { DepotId = id }, ct))
            .WithName("SaveDepotRule").Produces<bool>().ProducesProblem(StatusCodes.Status409Conflict);
        inventory.MapPut("/stock-level", (SetStockLevel body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SetStockLevel").Produces<bool>().ProducesProblem(StatusCodes.Status409Conflict);
    }

    // Stocuri › Documente: incarcari, descarcari, bonuri de consum, custodie, note de predare produse (02.05, S2 3.3–3.9; s2-03, s2-04).
    private static void MapDocuments(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/documents", ([AsParameters] GetStockDocuments query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetStockDocuments").Produces<IReadOnlyList<StockDocumentItemDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        inventory.MapGet("/documents/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetStockDocument(id), ct))
            .WithName("GetStockDocument").Produces<StockDocumentDto>().ProducesProblem(StatusCodes.Status404NotFound);
        inventory.MapGet("/movement-reasons", (ISender sender, CancellationToken ct) => sender.Send(new GetMovementReasons(), ct))
            .WithName("GetMovementReasons").Produces<IReadOnlyList<MovementReasonDto>>();
        inventory.MapPost("/documents", (SaveStockDocument body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateStockDocument").Saves();
        inventory.MapPut("/documents/{id:int}", (int id, SaveStockDocument body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateStockDocument").Saves();
        inventory.MapPost("/documents/{id:int}/validate", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ValidateStockDocument(id, body.RowVersion), ct))
            .WithName("ValidateStockDocument").Saves();
        inventory.MapPost("/documents/{id:int}/reverse", (int id, ReverseStockDocument body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("ReverseStockDocument").Saves();
        inventory.MapPost("/documents/{id:int}/cancel", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new CancelStockDocument(id, body.RowVersion), ct))
            .WithName("CancelStockDocument").Saves();
        inventory.MapGet("/movements", ([AsParameters] GetStockMovements query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetStockMovements").Produces<IReadOnlyList<StockMovementDto>>();
        inventory.MapGet("/variants", ([AsParameters] SearchStockVariants query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("SearchStockVariants").Produces<IReadOnlyList<StockVariantOptionDto>>();
        inventory.MapGet("/transit-shipments", ([AsParameters] GetTransitShipments query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetTransitShipments").Produces<IReadOnlyList<TransitShipmentDto>>();
        inventory.MapPost("/transit-shipments/{id:int}/receipt", (int id, CreateReceiptFromShipment body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { ShipmentId = id }, ct))
            .WithName("CreateReceiptFromShipment").Saves();
        inventory.MapPut("/documents/{id:int}/receipt", (int id, SaveReceipt body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("SaveReceipt").Saves();
        inventory.MapPost("/documents/{id:int}/approve", (int id, ApproveStockDocument body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("ApproveStockDocument").Saves();
    }

    // Stocuri › Costuri adaugate: documentul CA (02.05, S3 3.3; macheta s3-01).
    private static void MapLandedCosts(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/landed-costs", ([AsParameters] GetLandedCosts query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetLandedCosts").Produces<IReadOnlyList<LandedCostItemDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        inventory.MapGet("/landed-costs/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetLandedCost(id), ct))
            .WithName("GetLandedCost").Produces<LandedCostDto>().ProducesProblem(StatusCodes.Status404NotFound);
        inventory.MapPost("/landed-costs", (SaveLandedCost body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateLandedCost").Saves();
        inventory.MapPut("/landed-costs/{id:int}", (int id, SaveLandedCost body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateLandedCost").Saves();
        inventory.MapPost("/landed-costs/{id:int}/validate", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ValidateLandedCost(id, body.RowVersion), ct))
            .WithName("ValidateLandedCost").Saves();
        inventory.MapPost("/landed-costs/{id:int}/cancel", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new CancelLandedCost(id, body.RowVersion), ct))
            .WithName("CancelLandedCost").Saves();
    }

    // Stocuri › Pret cu amanuntul: modificarile de pret (MP), descarcarea lunara (DA), metoda de descarcare (02.05, S3 3.7; s3-03, s3-04).
    private static void MapRetail(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/retail-price-changes", ([AsParameters] GetRetailPriceChanges query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetRetailPriceChanges").Produces<IReadOnlyList<RetailPriceChangeItemDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        inventory.MapGet("/retail-price-changes/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetRetailPriceChange(id), ct))
            .WithName("GetRetailPriceChange").Produces<RetailPriceChangeDto>().ProducesProblem(StatusCodes.Status404NotFound);
        inventory.MapPost("/retail-price-changes", (SaveRetailPriceChange body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateRetailPriceChange").Saves();
        inventory.MapPut("/retail-price-changes/{id:int}", (int id, SaveRetailPriceChange body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("UpdateRetailPriceChange").Saves();
        inventory.MapPost("/retail-price-changes/{id:int}/validate", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ValidateRetailPriceChange(id, body.RowVersion), ct))
            .WithName("ValidateRetailPriceChange").Saves();
        inventory.MapPost("/retail-price-changes/{id:int}/cancel", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new CancelRetailPriceChange(id, body.RowVersion), ct))
            .WithName("CancelRetailPriceChange").Saves();
        inventory.MapGet("/retail-discharges", ([AsParameters] GetRetailDischarges query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetRetailDischarges").Produces<IReadOnlyList<RetailDischargeDto>>();
        inventory.MapGet("/retail-discharges/simulate", ([AsParameters] SimulateRetailDischarge query, ISender sender, CancellationToken ct) =>
                sender.Send(query, ct))
            .WithName("SimulateRetailDischarge").Produces<RetailDischargeDto>().ProducesProblem(StatusCodes.Status409Conflict);
        inventory.MapPost("/retail-discharges", (ValidateRetailDischarge body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("ValidateRetailDischarge").Produces<RetailDischargeDto>().ProducesProblem(StatusCodes.Status409Conflict);
        inventory.MapPut("/retail-discharge-method", (SetRetailDischargeMethod body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SetRetailDischargeMethod").Produces<bool>().ProducesProblem(StatusCodes.Status409Conflict);
    }

    // Stocuri › Evaluare: schimbarea metodei (s1-04), straturile (s3-05), coada de recalculare si ajustarile (s3-02).
    private static void MapValuation(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/valuation-changes", ([AsParameters] GetValuationChanges query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetValuationChanges").Produces<IReadOnlyList<ValuationChangeListItemDto>>();
        inventory.MapGet("/valuation-changes/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetValuationChange(id), ct))
            .WithName("GetValuationChange").Produces<ValuationChangeDto>().ProducesProblem(StatusCodes.Status404NotFound);
        inventory.MapPost("/valuation-changes", (SaveValuationChange body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = null }, ct))
            .WithName("CreateValuationChange").Saves();
        inventory.MapPut("/valuation-changes/{id:int}", (int id, SaveValuationChange body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = id }, ct))
            .WithName("UpdateValuationChange").Saves();
        inventory.MapPost("/valuation-changes/{id:int}/validate", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new ValidateValuationChange(id, body.RowVersion), ct))
            .WithName("ValidateValuationChange").Saves();
        inventory.MapPost("/valuation-changes/{id:int}/cancel", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new CancelValuationChange(id, body.RowVersion), ct))
            .WithName("CancelValuationChange").Saves();
        inventory.MapGet("/cost-layers", ([AsParameters] GetCostLayers query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetCostLayers").Produces<CostLayersDto>();
        inventory.MapGet("/cost-adjustments", ([AsParameters] GetCostAdjustments query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetCostAdjustments").Produces<IReadOnlyList<CostAdjustmentDto>>();
        inventory.MapGet("/cost-recalc-queue", (ISender sender, CancellationToken ct) => sender.Send(new GetCostRecalcQueue(), ct))
            .WithName("GetCostRecalcQueue").Produces<IReadOnlyList<CostRecalcQueueItemDto>>();
        inventory.MapPost("/cost-recalc", (RecalculateCost body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("RecalculateCost").Produces<int>().ProducesProblem(StatusCodes.Status403Forbidden);
    }

    // Stocuri › Loturi: lista, fisa, starea, atributele, propunerea FEFO, durata minima pe client (02.05, S4; s4-01…s4-03).
    private static void MapLots(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/lots", ([AsParameters] GetLots query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetLots").Produces<IReadOnlyList<LotItemDto>>().ProducesProblem(StatusCodes.Status403Forbidden);
        inventory.MapGet("/lots/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetLot(id), ct))
            .WithName("GetLot").Produces<LotDto>().ProducesProblem(StatusCodes.Status404NotFound);
        inventory.MapGet("/lots/propose", ([AsParameters] ProposeLots query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("ProposeLots").Produces<IReadOnlyList<Erp.Core.Contracts.LotAllocationDto>>();
        inventory.MapPost("/lots/{id:int}/status", (int id, ChangeLotStatus body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("ChangeLotStatus").Saves();
        inventory.MapPut("/lots/{id:int}", (int id, UpdateLot body, ISender sender, CancellationToken ct) => sender.Send(body with { Id = id }, ct))
            .WithName("UpdateLot").Saves();
        inventory.MapGet("/serials", ([AsParameters] GetSerials query, ISender sender, CancellationToken ct) => sender.Send(query, ct))
            .WithName("GetSerials").Produces<IReadOnlyList<SerialItemDto>>();
        inventory.MapGet("/serials/{id:int}", (int id, ISender sender, CancellationToken ct) => sender.Send(new GetSerial(id), ct))
            .WithName("GetSerial").Produces<SerialDto>().ProducesProblem(StatusCodes.Status404NotFound);
        inventory.MapPost("/serials/{id:int}/identifiers", (int id, EditSerialIdentifier body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { SerialId = id }, ct))
            .WithName("EditSerialIdentifier").Produces<bool>().ProducesProblem(StatusCodes.Status409Conflict);
        inventory.MapPut("/serial-identifier-rules", (SaveSerialIdentifierRule body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveSerialIdentifierRule").Produces<IReadOnlyList<SerialIdentifierRuleDto>>();
        inventory.MapPut("/partner-shelf-life-rules", (SavePartnerShelfLifeRule body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SavePartnerShelfLifeRule").Produces<IReadOnlyList<PartnerShelfLifeRuleDto>>();
    }

    // Nucleu › Articol › tab Stoc (02.05, S1 3.4; macheta s1-03).
    private static void MapItemSettings(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/items/{itemId:int}/stock-settings", (int itemId, ISender sender, CancellationToken ct) =>
                sender.Send(new GetItemStockSettings(itemId), ct))
            .WithName("GetItemStockSettings").Produces<ItemStockTabDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        inventory.MapPut("/items/{itemId:int}/stock-settings", (int itemId, SaveItemStockSettings body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { ItemId = itemId }, ct))
            .WithName("SaveItemStockSettings").Saves();
        inventory.MapPost("/items/{itemId:int}/warehouse-settings", (int itemId, SaveItemWarehouseSettings body, ISender sender, CancellationToken ct) =>
                sender.Send(body with { Id = null, ItemId = itemId }, ct))
            .WithName("CreateItemWarehouseSettings").Saves();
        inventory.MapPut("/items/{itemId:int}/warehouse-settings/{id:int}", (int itemId, int id, SaveItemWarehouseSettings body, ISender sender,
                CancellationToken ct) => sender.Send(body with { Id = id, ItemId = itemId }, ct))
            .WithName("UpdateItemWarehouseSettings").Saves();
        inventory.MapPost("/item-warehouse-settings/{id:int}/delete", (int id, InventoryRowVersionBody body, ISender sender, CancellationToken ct) =>
                sender.Send(new DeleteItemWarehouseSettings(id, body.RowVersion), ct))
            .WithName("DeleteItemWarehouseSettings").Produces<bool>().ProducesProblem(StatusCodes.Status409Conflict);
    }

    // Configurare › Stocuri › Generale (02.05, S1 3.3; ecranul in S7).
    private static void MapSettings(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/settings", (ISender sender, CancellationToken ct) => sender.Send(new GetStockSettings(), ct))
            .WithName("GetStockSettings").Produces<StockSettingsDto>().ProducesProblem(StatusCodes.Status403Forbidden);
        inventory.MapPut("/settings", (SaveStockSettings body, ISender sender, CancellationToken ct) => sender.Send(body, ct))
            .WithName("SaveStockSettings").Saves();
    }

    private static RouteHandlerBuilder Saves(this RouteHandlerBuilder builder) => builder
        .Produces<InventorySaved>()
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);
}

/// <summary>Corpul unei actiuni pe gestiune: versiunea vazuta de utilizator.</summary>
public sealed record InventoryRowVersionBody(byte[] RowVersion);
