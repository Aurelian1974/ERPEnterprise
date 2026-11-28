using Erp.Core.Contracts;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Lots;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Seriile pe SQL Server real (02.05, S4 3.4): o serie e o bucata, intr-o singura gestiune si vanduta o data; citirea doar la iesire;
/// identificatorii obligatorii, valizi si unici (IMEI) si cautarea dupa ei; articolele fara urmarire nu cer nimic.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SerialIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static long nextSource = 1_900_000;

    private async Task<int> NewDepot() =>
        (await Send(new SaveWarehouse(null, Unique("SR-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null, null, null, null, 1,
            null, WarehousePurpose.Regular, null))).Id;

    private async Task<(int Item, int Variant)> SerialItem(byte captureMode = 1)
    {
        var (item, ids) = await NewItem(Unique("SR"), true, "U");
        await Send(new SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.Serial, null, null, null, false, null));
        await Execute("UPDATE inv.ItemStockSettings SET SerialCaptureMode = @M, WarrantyMonths = 24 WHERE ItemId = @I", new { M = captureMode, I = item });
        return (item, ids[0]);
    }

    private static StockMovementRequest Piece(string type, int warehouse, int variant, decimal qty, string? serial, long? source = null, byte role = 1,
        params SerialIdentifierValue[] identifiers) =>
        new(source ?? Interlocked.Increment(ref nextSource), role, type, null, warehouse, variant, qty, qty > 0 ? 100m : null, null, null, null, null, null, null, null)
        {
            NewSerialNo = serial, SerialIdentifiers = identifiers,
        };

    private Task<StockPostingResult> Post(int? partner, params StockMovementRequest[] lines) =>
        InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            Interlocked.Increment(ref nextSource), "T", Today, Today, partner, false, lines), Ct));

    private Task<(byte Status, int? WarehouseId, DateTime? Warranty)> Serial(int variant, string serial) =>
        Query<(byte, int?, DateTime?)>("SELECT Status, WarehouseId, WarrantyUntil FROM inv.Serial WHERE ItemVariantId = @V AND SerialNo = @S",
            new { V = variant, S = serial });

    [Fact]
    [Trait("Criteriu", "02.05-S4-A7")]
    public async Task A_serial_is_in_one_warehouse_at_a_time_and_is_sold_once()
    {
        var a = await NewDepot();
        var b = await NewDepot();
        var (_, variant) = await SerialItem();
        await Post(null, Piece("LOAD", a, variant, 1, "SN-1"), Piece("LOAD", a, variant, 1, "SN-2"));
        Assert.Equal("INV_SERIAL_DUPLICATE", (await Assert.ThrowsAsync<ErpException>(() => Post(null, Piece("LOAD", b, variant, 1, "SN-1")))).Code);
        Assert.Equal("INV_SERIAL_DUPLICATE", (await Assert.ThrowsAsync<ErpException>(() =>
            Post(null, Piece("UNLOAD", a, variant, -1, "SN-2"), Piece("UNLOAD", a, variant, -1, "SN-2")))).Code);
        Assert.Equal("INV_SERIAL_REQUIRED", (await Assert.ThrowsAsync<ErpException>(() => Post(null, Piece("LOAD", a, variant, 2, null)))).Code);

        var line = Interlocked.Increment(ref nextSource);
        await Post(null, Piece("TRANSFER_OUT", a, variant, -1, "SN-1", line), Piece("TRANSFER_IN", b, variant, 1, null, line, 2));
        Assert.Equal(((byte)1, b), ((await Serial(variant, "SN-1")).Status, (await Serial(variant, "SN-1")).WarehouseId));
        Assert.Equal("INV_SERIAL_NOT_IN_STOCK", (await Assert.ThrowsAsync<ErpException>(() => Post(null, Piece("UNLOAD", a, variant, -1, "SN-1")))).Code);

        await Post(Database.LogisticPartnerId, Piece("ISSUE_SALE", b, variant, -1, "SN-1"));
        var sold = await Serial(variant, "SN-1");
        Assert.Equal((byte)3, sold.Status);
        Assert.Equal(Today.AddMonths(24), DateOnly.FromDateTime(sold.Warranty!.Value));
        Assert.Equal("INV_SERIAL_NOT_IN_STOCK", (await Assert.ThrowsAsync<ErpException>(() =>
            Post(Database.LogisticPartnerId, Piece("ISSUE_SALE", b, variant, -1, "SN-1")))).Code);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%SerieVanduta' AND Payload LIKE N'%\"serialNo\":\"SN-1\"%'"));

        // Returul clientului o readuce in stoc; o serie necunoscuta nu se poate returna.
        await Post(Database.LogisticPartnerId, Piece("RETURN_FROM_CUSTOMER", b, variant, 1, "SN-1"));
        Assert.Equal((byte)4, (await Serial(variant, "SN-1")).Status);
        Assert.Equal("INV_SERIAL_STATUS_INVALID", (await Assert.ThrowsAsync<ErpException>(() =>
            Post(Database.LogisticPartnerId, Piece("RETURN_FROM_CUSTOMER", b, variant, 1, "SN-NEW")))).Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A8")]
    public async Task With_capture_at_issue_the_receipt_has_no_serials_and_the_sale_creates_them_sold()
    {
        var warehouse = await NewDepot();
        var (_, variant) = await SerialItem(captureMode: 2);
        await Post(null, Piece("LOAD", warehouse, variant, 5, null));
        await Post(Database.LogisticPartnerId, Piece("ISSUE_SALE", warehouse, variant, -1, "CAP-1"), Piece("ISSUE_SALE", warehouse, variant, -1, "CAP-2"));
        Assert.Equal((byte)3, (await Serial(variant, "CAP-1")).Status);
        Assert.Equal(3m, await Query<decimal>("SELECT SUM(QtyOnHand) FROM inv.Stock WHERE ItemVariantId = @V", new { V = variant }));
        Assert.Equal("INV_SERIAL_DUPLICATE", (await Assert.ThrowsAsync<ErpException>(() =>
            Post(Database.LogisticPartnerId, Piece("ISSUE_SALE", warehouse, variant, -1, "CAP-1")))).Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S4-A14")]
    public async Task A_phone_needs_a_valid_and_unique_imei_and_is_found_by_it_while_nuts_need_nothing()
    {
        var warehouse = await NewDepot();
        var (item, variant) = await SerialItem();
        await Send(new SaveSerialIdentifierRule(item, null, "IMEI", 1, true, false, true, true, false));
        var imei = new SerialIdentifierValue("IMEI", "490154203237518");

        Assert.Equal("INV_SERIAL_IDENTIFIER_REQUIRED", (await Assert.ThrowsAsync<ErpException>(() => Post(null, Piece("LOAD", warehouse, variant, 1, "PH-1")))).Code);
        Assert.Equal("INV_SERIAL_IDENTIFIER_INVALID", (await Assert.ThrowsAsync<ErpException>(() =>
            Post(null, Piece("LOAD", warehouse, variant, 1, "PH-1", identifiers: new SerialIdentifierValue("IMEI", "490154203237519"))))).Code);
        await Post(null, Piece("LOAD", warehouse, variant, 1, "PH-1", identifiers: imei));
        Assert.Equal("INV_SERIAL_IDENTIFIER_DUPLICATE", (await Assert.ThrowsAsync<ErpException>(() =>
            Post(null, Piece("LOAD", warehouse, variant, 1, "PH-2", identifiers: imei)))).Code);

        var found = await WithService<IStockLedger, IReadOnlyList<SerialInfoDto>>(s => s.FindSerialByIdentifierAsync("490154203237518", null, Ct));
        Assert.Equal("PH-1", Assert.Single(found).SerialNo);
        Assert.Equal("PH-1", (await WithService<IStockLedger, SerialInfoDto?>(s => s.FindSerialAsync(variant, "PH-1", Ct)))?.SerialNo);
        var card = await Send(new GetSerial(found[0].SerialId));
        Assert.Equal(("IMEI", "490154203237518"), (card.Identifiers.Single().TypeCode, card.Identifiers.Single().Value));

        // 10.000 de piulite (fara urmarire): fara lot si fara serie.
        var (_, nuts) = await NewItem(Unique("PIU"), true, "M10");
        var receipt = await Post(null, Piece("LOAD", warehouse, nuts[0], 10_000, null));
        Assert.Null(await Query<int?>("SELECT SerialId FROM inv.StockMovement WHERE Id = @Id", new { Id = receipt.Movements.Single().MovementId }));
    }
}
