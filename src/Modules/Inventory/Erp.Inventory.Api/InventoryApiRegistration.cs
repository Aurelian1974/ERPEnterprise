using Erp.Inventory.Application;
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
