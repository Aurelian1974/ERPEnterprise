using Erp.Core.Contracts;
using Erp.Inventory.Application.Costing;
using Erp.Inventory.Application.Items;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Gestiunile la pret cu amanuntul pe SQL Server real (02.05, S3 3.7): pretul stabilit prin MP, valoarea cu amanuntul si TVA-ul pe miscari,
/// metoda 2 (costul pe fiecare vanzare), descarcarea lunara prin K (DA, metoda 1), refuzurile.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RetailValuationIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);
    private static long nextSource = 900000;

    private async Task<int> NewStore(byte? method = null)
    {
        var id = (await Send(new SaveWarehouse(null, Unique("MG-"), "Magazin", Database.ClujStoreId, WarehouseKind.Store, ValuationBasis.RetailPrice, 7, null,
            null, null, 1, null, WarehousePurpose.Regular, null))).Id;
        if (method is not null)
        {
            await Send(new SetRetailDischargeMethod(id, method));
        }

        return id;
    }

    /// <summary>O varianta FIFO cu cota redusa de TVA (11%).</summary>
    private async Task<int> Variant()
    {
        var (item, variants) = await NewItem(Unique("RT"), true, "U");
        await Send(new SaveItemStockSettings(item, null, ValuationMethod.Fifo, TrackingMode.None, null, null, null, false, null));
        await Execute("UPDATE core.Item SET DefaultVatRateId = (SELECT TOP (1) Id FROM core.VatRate WHERE Code = N'R11') WHERE Id = @Id", new { Id = item });
        return variants[0];
    }

    private async Task SetPrice(int warehouse, int variant, decimal price, DateOnly date)
    {
        var saved = await Send(new SaveRetailPriceChange(null, warehouse, date, null,
            [new RetailPriceChangeLineInput(variant, price.ToString(System.Globalization.CultureInfo.InvariantCulture))], null));
        await Send(new ValidateRetailPriceChange(saved.Id, saved.RowVersion));
    }

    private static StockMovementRequest Line(string type, int warehouse, int variant, decimal qty, decimal? cost = null) =>
        new(Interlocked.Increment(ref nextSource), 1, type, null, warehouse, variant, qty, cost, null, null, null, null, null, null, null);

    private Task<StockPostingResult> Post(DateOnly date, params StockMovementRequest[] lines) =>
        InTransaction(sp => sp.GetRequiredService<IStockLedger>().PostAsync(new StockPostingRequest(Database.CompanyId, "test", "TEST",
            Interlocked.Increment(ref nextSource), "T", date, date, null, false, lines), Ct));

    private Task<(decimal? Retail, decimal? Vat)> Retail(long movement) =>
        Query<(decimal?, decimal?)>("SELECT RetailAmount, RetailVatAmount FROM inv.StockMovement WHERE Id = @Id", new { Id = movement });

    [Fact]
    [Trait("Criteriu", "02.05-S3-A13")]
    public async Task With_method_2_a_receipt_of_12_99_carries_cost_markup_and_vat_and_no_discharge_document_is_made()
    {
        var store = await NewStore(2);
        var variant = await Variant();
        await SetPrice(store, variant, 12.99m, Today.AddDays(-3));
        await Post(Today.AddDays(-2), Line("LOAD", store, variant, 1, 8.20m));
        var sale = (await Post(Today, Line("POS_SALE", store, variant, -1))).Movements.Single();

        var (retail, vat) = await Retail(sale.MovementId);
        Assert.Equal(-8.20m, sale.CostAmount);
        Assert.Equal((-12.99m, -1.29m), (retail, vat));
        // C2: 607 = 371 costul 8,20; 378 = 371 adaosul (pret fara TVA − cost) 3,50; 4428 = 371 TVA-ul 1,29.
        Assert.Equal(3.50m, -retail!.Value + vat!.Value - -sale.CostAmount);
        Assert.Equal(1, await Query<int>("""
            SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%MiscareStocValidata%' AND Payload LIKE @M AND Payload LIKE N'%"retailDischargeMethod":2%'
            """, new { M = $"%\"movementId\":{sale.MovementId},%" }));

        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidateRetailDischarge(store, Today)));
        Assert.Equal("INV_RETAIL_DISCHARGE_NOT_APPLICABLE", refused.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-3.7")]
    public async Task A_receipt_in_a_retail_store_needs_a_price_and_a_price_change_revalues_the_stock()
    {
        var store = await NewStore();
        var variant = await Variant();
        var missing = await Assert.ThrowsAsync<ErpException>(() => Post(Today.AddDays(-2), Line("LOAD", store, variant, 10, 5)));
        Assert.Equal("INV_RETAIL_PRICE_MISSING", missing.Code);

        await SetPrice(store, variant, 11.10m, Today.AddDays(-2));
        var receipt = (await Post(Today.AddDays(-1), Line("LOAD", store, variant, 10, 5))).Movements.Single();
        Assert.Equal((111.00m, 11.00m), await Retail(receipt.MovementId));

        var backdated = await Assert.ThrowsAsync<ErpException>(() => SetPrice(store, variant, 9.99m, Today.AddDays(-2)));
        Assert.Equal("INV_RETAIL_PRICE_CHANGE_BACKDATED", backdated.Code);

        var change = await Send(new SaveRetailPriceChange(null, store, Today, null, [new RetailPriceChangeLineInput(variant, "13.32")], null));
        await Send(new ValidateRetailPriceChange(change.Id, change.RowVersion));
        var document = await Send(new GetRetailPriceChange(change.Id));
        Assert.Equal(("22.20", "2.20"), (document.DiffAmount, document.DiffVatAmount));
        Assert.Equal(("11.1", "10"), (document.Lines[0].OldPrice, document.Lines[0].Qty));
        Assert.Equal(133.20m, await Query<decimal>("SELECT SUM(RetailValueOnHand) FROM inv.Stock WHERE WarehouseId = @W", new { W = store }));

        var sale = (await Post(Today, Line("POS_SALE", store, variant, -2))).Movements.Single();
        Assert.Equal((-26.64m, -2.64m), await Retail(sale.MovementId));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S3-4.4")]
    public async Task The_monthly_discharge_uses_k_from_the_store_movements_once_per_month()
    {
        var store = await NewStore(1);
        var variant = await Variant();
        var month = new DateOnly(Today.Year, Today.Month, 1);
        await SetPrice(store, variant, 12.21m, month);
        // 100 × 12,21 = 1.221,00 cu TVA (121,00), cost 800,00: adaosul intrat 300,00; vandute 40 = 488,40 cu TVA 48,40.
        await Post(month, Line("LOAD", store, variant, 100, 8));
        await Post(month, Line("POS_SALE", store, variant, -40));

        var simulated = await Send(new SimulateRetailDischarge(store, month));
        Assert.Null(simulated.Id);
        Assert.Equal(("1221.00", "488.40", "300.00", "121.00", "48.40"), (simulated.RetailIn, simulated.RetailOut, simulated.MarkupIn, simulated.VatIn,
            simulated.SalesVat));
        Assert.Equal("440.00", simulated.SalesNet);
        Assert.Equal(0.272727m, simulated.K);
        Assert.Equal(("120.00", "320.00"), (simulated.Markup, simulated.Cost));

        var validated = await Send(new ValidateRetailDischarge(store, month));
        Assert.NotNull(validated.Number);
        var again = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidateRetailDischarge(store, month)));
        Assert.Equal("INV_RETAIL_DISCHARGE_EXISTS", again.Code);
        Assert.Equal(1, await Query<int>("SELECT COUNT(*) FROM core.Outbox WHERE EventType LIKE N'%DescarcareAmanuntValidata%' AND Payload LIKE @W",
            new { W = $"%\"warehouseId\":{store},%" }));

        var change = await Assert.ThrowsAsync<ErpException>(() => Send(new SetRetailDischargeMethod(store, 2)));
        Assert.Equal("INV_RETAIL_METHOD_CHANGE_NOT_ALLOWED", change.Code);
    }
}
