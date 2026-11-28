using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Documents;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Notele de predare si de primire pe SQL Server real (02.05, S2 3.4, 4.2, 4.3, 4.6): o etapa in aceeasi locatie, doua etape prin TRANZIT intre
/// locatii, primirea cu diferentele si rezolvarea lor, starea expeditiei, valoarea firmei neschimbata, stornarea.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TransferIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);

    private async Task<int> NewDepot(int locationId) =>
        (await Send(new SaveWarehouse(null, Unique("TR-"), "Gestiune", locationId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1, null,
            WarehousePurpose.Regular, null))).Id;

    private async Task<int> Transit() => (await Send(new GetWarehouses(Kind: WarehouseKind.Transit))).Warehouses.Single().Id;

    private static StockDocumentLineInput Line(int variant, string qty, string? cost = null) =>
        new(variant, null, qty, "1", cost, null, null, null, null, null, null);

    private async Task Load(int warehouse, int variant, string qty, string cost)
    {
        var reason = (await Send(new GetMovementReasons())).Single(r => r.Code == "OPENING").Id;
        var draft = await Send(new SaveStockDocument(null, StockDocumentTypes.Load, Today, null, null, null, null, warehouse, reason, null, null, null, null,
            null, [Line(variant, qty, cost)], null));
        await Send(new ValidateStockDocument(draft.Id, draft.RowVersion));
    }

    private async Task<StockDocumentDto> Transfer(int from, int to, int variant, string qty)
    {
        var draft = await Send(new SaveStockDocument(null, StockDocumentTypes.TransferNote, Today, null, null, from, null, to, null, null, null, null, null,
            null, [Line(variant, qty)], null));
        var validated = await Send(new ValidateStockDocument(draft.Id, draft.RowVersion));
        return await Send(new GetStockDocument(validated.Id));
    }

    private Task<(decimal Qty, decimal Cost)> Stock(int warehouse, int variant) =>
        Query<(decimal, decimal)>("SELECT ISNULL(SUM(QtyOnHand), 0), ISNULL(SUM(CostOnHand), 0) FROM inv.Stock WHERE WarehouseId = @W AND ItemVariantId = @V",
            new { W = warehouse, V = variant });

    private Task<decimal> CompanyValue(int variant) =>
        Query<decimal>("SELECT ISNULL(SUM(CostOnHand), 0) FROM inv.Stock WHERE CompanyId = @C AND ItemVariantId = @V", new { C = Database.CompanyId, V = variant });

    private async Task<StockDocumentDto> Receive(int shipmentId, Func<StockDocumentLineDto, ReceiptLineInput> line)
    {
        var created = await Send(new CreateReceiptFromShipment(shipmentId, Today));
        var receipt = await Send(new GetStockDocument(created.Id));
        var saved = await Send(new SaveReceipt(created.Id, null, receipt.Lines.Select(line).ToList(), created.RowVersion));
        var validated = await Send(new ValidateStockDocument(created.Id, saved.RowVersion));
        return await Send(new GetStockDocument(validated.Id));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-4.2")]
    public async Task A_transfer_in_the_same_location_moves_the_stock_in_one_stage_at_the_issuing_cost()
    {
        var a = await NewDepot(Database.ClujDepotId);
        var b = await NewDepot(Database.ClujDepotId);
        var (_, variants) = await NewItem(Unique("T1"), true, "U");
        await Load(a, variants[0], "10", "3");

        var note = await Transfer(a, b, variants[0], "4");

        Assert.Equal(((byte?)1, (int?)null), (note.TransferMode, note.TransitShipmentId));
        Assert.Equal((6m, 18m), await Stock(a, variants[0]));
        Assert.Equal((4m, 12m), await Stock(b, variants[0]));
        var same = await Assert.ThrowsAsync<ErpException>(() => Transfer(a, a, variants[0], "1"));
        Assert.Equal("INV_WAREHOUSE_PAIR_INVALID", same.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-A5")]
    [Trait("Criteriu", "02.05-S2-A6")]
    public async Task A_transfer_between_locations_goes_through_transit_and_a_charged_shortage_closes_the_shipment()
    {
        var cluj = await NewDepot(Database.ClujDepotId);
        var ploiesti = await NewDepot(Database.PloiestiId);
        var transit = await Transit();
        var (_, variants) = await NewItem(Unique("T2"), true, "U");
        await Load(cluj, variants[0], "100", "2.5");
        var value = await CompanyValue(variants[0]);

        var note = await Transfer(cluj, ploiesti, variants[0], "50");
        Assert.Equal((byte?)2, note.TransferMode);
        Assert.Equal((50m, 125m), await Stock(transit, variants[0]));
        Assert.Equal(value, await CompanyValue(variants[0]));
        var shipment = Assert.Single(await Send(new GetTransitShipments(ploiesti)));
        Assert.Equal(((byte)1, note.Id), (shipment.Status, shipment.NpDocumentId));

        var tooMuch = await Assert.ThrowsAsync<ErpException>(async () =>
        {
            var created = await Send(new CreateReceiptFromShipment(shipment.Id, Today));
            var receipt = await Send(new GetStockDocument(created.Id));
            await Send(new SaveReceipt(created.Id, null, [new ReceiptLineInput(receipt.Lines.Single().Id, "51", null)], created.RowVersion));
        });
        Assert.Equal("INV_RECEIPT_EXCEEDS_SHIPMENT", tooMuch.Code);
        var drafts = await Send(new GetStockDocuments(StockDocumentTypes.ReceiptNote, 1));
        foreach (var draft in drafts)
        {
            await Send(new CancelStockDocument(draft.Id, (await Send(new GetStockDocument(draft.Id))).RowVersion));
        }

        var received = await Receive(shipment.Id, l => new ReceiptLineInput(l.Id, "26", 3));
        Assert.Equal("26", received.Lines.Single().ReceivedQtyBase);
        Assert.Equal((26m, 65m), await Stock(ploiesti, variants[0]));
        Assert.Equal((0m, 0m), await Stock(transit, variants[0]));
        Assert.Equal(4, (await Send(new GetTransitShipments(ploiesti, false))).Single(s => s.Id == shipment.Id).Status);
        Assert.Equal(-24m, await Query<decimal>("""
            SELECT SUM(m.QtyBase) FROM inv.StockMovement AS m JOIN inv.MovementReason AS r ON r.Id = m.ReasonId
            WHERE m.SourceModule = 'inv-diff' AND m.SourceDocumentId = @Id AND r.Code = 'SHORTAGE_CHARGED'
            """, new { received.Id }));
        Assert.Empty(await Send(new GetTransitShipments(ploiesti)));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-3.4")]
    public async Task Partial_receipts_returns_and_surpluses_are_resolved_until_the_shipment_is_received()
    {
        var cluj = await NewDepot(Database.ClujDepotId);
        var ploiesti = await NewDepot(Database.PloiestiId);
        var transit = await Transit();
        var (_, variants) = await NewItem(Unique("T3"), true, "A", "B");
        await Load(cluj, variants[0], "30", "4");
        await Load(cluj, variants[1], "10", "1");

        var draft = await Send(new SaveStockDocument(null, StockDocumentTypes.TransferNote, Today, null, null, cluj, null, ploiesti, null, null, null, null,
            null, null, [Line(variants[0], "20"), Line(variants[1], "10")], null));
        var note = await Send(new GetStockDocument((await Send(new ValidateStockDocument(draft.Id, draft.RowVersion))).Id));
        var shipmentId = note.TransitShipmentId!.Value;

        // Prima primire: 15 din 20 (5 raman in tranzit) si 12 din 10 (plus 2).
        await Receive(shipmentId, l => l.ItemVariantId == variants[0] ? new ReceiptLineInput(l.Id, "15", 1) : new ReceiptLineInput(l.Id, "12", 5));
        Assert.Equal(2, (await Send(new GetTransitShipments(ploiesti))).Single(s => s.Id == shipmentId).Status);
        Assert.Equal((5m, 20m), await Stock(transit, variants[0]));
        Assert.Equal((12m, 12m), await Stock(ploiesti, variants[1]));

        // A doua primire: restul de 5 se returneaza expeditorului.
        var second = await Receive(shipmentId, l => new ReceiptLineInput(l.Id, "0", 2));
        Assert.Single(second.Lines);
        Assert.Equal((15m, 60m), await Stock(cluj, variants[0]));
        Assert.Equal((0m, 0m), await Stock(transit, variants[0]));
        Assert.Equal(4, (await Send(new GetTransitShipments(ploiesti, false))).Single(s => s.Id == shipmentId).Status);
        var noRest = await Assert.ThrowsAsync<ErpException>(() => Send(new CreateReceiptFromShipment(shipmentId, Today)));
        Assert.Equal("INV_REVERSAL_INVALID", noRest.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-4.6")]
    public async Task A_shipped_transfer_is_reversed_only_before_any_receipt()
    {
        var cluj = await NewDepot(Database.ClujDepotId);
        var ploiesti = await NewDepot(Database.PloiestiId);
        var (_, variants) = await NewItem(Unique("T4"), true, "U");
        await Load(cluj, variants[0], "10", "5");

        var first = await Transfer(cluj, ploiesti, variants[0], "4");
        await Send(new ReverseStockDocument(first.Id, null, first.RowVersion));
        Assert.Equal((10m, 50m), await Stock(cluj, variants[0]));
        Assert.Equal(5, (await Send(new GetTransitShipments(ploiesti, false))).Single(s => s.NpDocumentId == first.Id).Status);

        var second = await Transfer(cluj, ploiesti, variants[0], "4");
        await Receive(second.TransitShipmentId!.Value, l => new ReceiptLineInput(l.Id, "4", null));
        var late = await Assert.ThrowsAsync<ErpException>(async () =>
            await Send(new ReverseStockDocument(second.Id, null, (await Send(new GetStockDocument(second.Id))).RowVersion)));
        Assert.Equal("INV_REVERSAL_INVALID", late.Code);
        Assert.Equal((4m, 20m), await Stock(ploiesti, variants[0]));
    }
}
