using Erp.Inventory.Application;
using Erp.Inventory.Application.Costing;
using Erp.Inventory.Application.Documents;
using Erp.Inventory.Application.Movements;
using Erp.Inventory.Contracts;
using Erp.Inventory.Application.Items;
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
