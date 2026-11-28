using Erp.Core.Contracts;
using Erp.Inventory.Application;
using Erp.Inventory.Application.Documents;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Contracts;
using Erp.Inventory.Domain;

namespace Erp.Inventory.Tests.Sql;

/// <summary>
/// Documentele proprii ale Stocurilor pe SQL Server real (02.05, S2 3.3, 3.5–3.9, 4.4–4.6): incarcarea, descarcarea cu aprobare si
/// proces-verbal, bonul de consum, custodia, stornarea la ban, anularea si drepturile pe gestiuni.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public sealed class StockDocumentIntegrationTests(InventoryDatabase database) : InventoryTestBase(database), IClassFixture<InventoryDatabase>
{
    private static readonly DateOnly Today = BusinessDay.Today(TimeProvider.System);

    private async Task<(int Id, int Dimension)> NewDepot(int? costCenterId = null)
    {
        var saved = await Send(new SaveWarehouse(null, Unique("SD-"), "Gestiune", Database.ClujDepotId, WarehouseKind.Depot, ValuationBasis.Cost, null,
            null, null, costCenterId, 1, null, WarehousePurpose.Regular, null));
        return (saved.Id, (await Send(new GetWarehouse(saved.Id))).DimensionValueId);
    }

    private async Task<int> Reason(string code) => (await Send(new GetMovementReasons())).Single(r => r.Code == code).Id;

    private static StockDocumentLineInput Line(int variant, string qty, string? cost = null) =>
        new(variant, null, qty, "1", cost, null, null, null, null, null, null);

    private async Task<InventorySaved> Load(int warehouse, int variant, string qty, string cost)
    {
        var draft = await Send(new SaveStockDocument(null, StockDocumentTypes.Load, Today, null, null, null, null, warehouse, await Reason("OPENING"), null,
            null, null, null, null, [Line(variant, qty, cost)], null));
        return await Send(new ValidateStockDocument(draft.Id, draft.RowVersion));
    }

    private Task Attach(int documentId) =>
        Execute("""
            INSERT INTO core.Attachment (CompanyId, EntityType, EntityId, FileName, ContentType, Size, Sha256, StorageKind, Content, CreatedBy)
            VALUES (@CompanyId, 'Inv.StockDocument', @Id, N'pv.pdf', 'application/pdf', 3, HASHBYTES('SHA2_256', 0x010203), 1, 0x010203, 1);
            """, new { Database.CompanyId, Id = documentId });

    private Task<(decimal Qty, decimal Cost)> Stock(int warehouse, int variant) =>
        Query<(decimal, decimal)>("SELECT ISNULL(SUM(QtyOnHand), 0), ISNULL(SUM(CostOnHand), 0) FROM inv.Stock WHERE WarehouseId = @W AND ItemVariantId = @V",
            new { W = warehouse, V = variant });

    [Fact]
    [Trait("Criteriu", "02.05-S2-A11")]
    public async Task An_opening_load_gets_a_number_and_puts_the_stock_at_the_entered_cost()
    {
        var (warehouse, _) = await NewDepot();
        var (_, variants) = await NewItem(Unique("OP"), true, "U");
        var noCost = await Assert.ThrowsAsync<ErpException>(() => Reason("OPENING").ContinueWith(r => Send(new SaveStockDocument(null, StockDocumentTypes.Load,
            Today, null, null, null, null, warehouse, r.Result, null, null, null, null, null, [Line(variants[0], "10")], null))).Unwrap());
        Assert.Equal("INV_UNIT_COST_REQUIRED", noCost.Code);

        var validated = await Load(warehouse, variants[0], "10", "12.5");
        var document = await Send(new GetStockDocument(validated.Id));
        Assert.Equal(((byte)2, "125.00", "12.5"), (document.Status, document.TotalCost, document.Lines.Single().UnitCostPosted));
        Assert.False(string.IsNullOrEmpty(document.Number));
        Assert.Equal((10m, 125m), await Stock(warehouse, variants[0]));
        Assert.Contains(await Send(new GetStockDocuments(StockDocumentTypes.Load)), d => d.Id == validated.Id && d.Status == 2);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-A7")]
    public async Task A_scrap_above_the_threshold_needs_approval_by_someone_else_and_the_signed_report()
    {
        var (warehouse, _) = await NewDepot();
        var (_, variants) = await NewItem(Unique("SC"), true, "U");
        await Load(warehouse, variants[0], "100", "62");

        var draft = await Send(new SaveStockDocument(null, StockDocumentTypes.Unload, Today, null, null, warehouse, null, null, await Reason("SCRAP"), null,
            null, null, null, "Degradate la descărcare", [Line(variants[0], "100")], null));
        var document = await Send(new GetStockDocument(draft.Id));
        Assert.Equal((byte)1, document.ApprovalStatus);

        var unapproved = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidateStockDocument(draft.Id, draft.RowVersion)));
        Assert.Equal("INV_APPROVAL_REQUIRED", unapproved.Code);
        Permissions.Add(InventoryPermissions.LoadUnloadApprove);
        var self = await Assert.ThrowsAsync<ErpException>(() => Send(new ApproveStockDocument(draft.Id, true, null, draft.RowVersion)));
        Assert.Equal("INV_APPROVER_IS_AUTHOR", self.Code);
        var approved = await Send(new ApproveStockDocument(draft.Id, true, "Aprobat", draft.RowVersion), userId: Database.KeeperUserId);

        var noReport = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidateStockDocument(draft.Id, approved.RowVersion)));
        Assert.Equal("INV_ATTACHMENT_REQUIRED", noReport.Code);
        await Attach(draft.Id);
        await Send(new ValidateStockDocument(draft.Id, approved.RowVersion));
        Assert.Equal((0m, 0m), await Stock(warehouse, variants[0]));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-A8")]
    public async Task Reversing_an_issue_restores_the_stock_value_exactly()
    {
        var (warehouse, _) = await NewDepot();
        var (_, variants) = await NewItem(Unique("RV"), true, "U");
        await Load(warehouse, variants[0], "3", "10");
        await Load(warehouse, variants[0], "4", "11.75");
        var before = await Stock(warehouse, variants[0]);

        var draft = await Send(new SaveStockDocument(null, StockDocumentTypes.Unload, Today, null, null, warehouse, null, null, await Reason("PROTOCOL"), null,
            null, null, null, null, [Line(variants[0], "5")], null));
        var validated = await Send(new ValidateStockDocument(draft.Id, draft.RowVersion));
        Assert.NotEqual(before, await Stock(warehouse, variants[0]));

        var reversal = await Send(new ReverseStockDocument(validated.Id, null, validated.RowVersion));
        Assert.Equal(before, await Stock(warehouse, variants[0]));
        var original = await Send(new GetStockDocument(validated.Id));
        var storno = await Send(new GetStockDocument(reversal.Id));
        Assert.Equal(((byte)3, reversal.Id), (original.Status, original.ReversedByDocumentId!.Value));
        Assert.Equal((true, (byte)2, validated.Id, "-5"), (storno.IsReversal, storno.Status, storno.RelatedDocumentId!.Value, storno.Lines.Single().Qty));
        var again = await Assert.ThrowsAsync<ErpException>(() => Send(new ReverseStockDocument(validated.Id, null, original.RowVersion)));
        Assert.Equal("INV_REVERSAL_INVALID", again.Code);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-3.7")]
    public async Task A_consumption_voucher_takes_the_warehouse_cost_center_or_is_refused()
    {
        var costCenter = await Query<int>("""
            INSERT INTO core.DimensionValue (CompanyId, DimensionType, Code, Name, IsActive, CreatedBy) VALUES (@C, 'COST_CENTER', @Code, N'Logistică', 1, 1);
            SELECT CAST(SCOPE_IDENTITY() AS int);
            """, new { C = Database.CompanyId, Code = Unique("LOG") });
        var (withCenter, _) = await NewDepot(costCenter);
        var (withoutCenter, _) = await NewDepot();
        var (_, variants) = await NewItem(Unique("BC"), true, "U");
        await Load(withCenter, variants[0], "5", "2");

        var refused = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveStockDocument(null, StockDocumentTypes.Consumption, Today, null, null,
            withoutCenter, null, null, null, null, null, null, null, null, [Line(variants[0], "1")], null)));
        Assert.Equal("INV_COST_CENTER_REQUIRED", refused.Code);

        var draft = await Send(new SaveStockDocument(null, StockDocumentTypes.Consumption, Today, null, null, withCenter, null, null, null, null, null, null,
            null, null, [Line(variants[0], "2")], null));
        Assert.Equal(costCenter, (await Send(new GetStockDocument(draft.Id))).CostCenterId);
        await Send(new ValidateStockDocument(draft.Id, draft.RowVersion));
        Assert.Equal(costCenter, await Query<int>("SELECT CostCenterId FROM inv.StockMovement WHERE SourceModule = 'inv' AND SourceDocumentId = @Id",
            new { draft.Id }));
    }

    [Fact]
    [Trait("Criteriu", "02.05-S1-A5")]
    public async Task A_user_scoped_to_one_warehouse_cannot_validate_a_voucher_from_another()
    {
        var (mine, mineDimension) = await NewDepot();
        var (other, _) = await NewDepot();
        var (_, variants) = await NewItem(Unique("SP"), true, "U");
        await Load(other, variants[0], "5", "2");
        var draft = await Send(new SaveStockDocument(null, StockDocumentTypes.Unload, Today, null, null, other, null, null, await Reason("PROTOCOL"), null,
            null, null, null, null, [Line(variants[0], "1")], null));

        WarehouseScope = [mineDimension];
        Assert.DoesNotContain(await Send(new GetStockDocuments()), d => d.Id == draft.Id);
        var hidden = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidateStockDocument(draft.Id, draft.RowVersion)));
        Assert.Equal("INV_DOCUMENT_NOT_FOUND", hidden.Code);
        Assert.True(mine > 0);
    }

    [Fact]
    [Trait("Criteriu", "02.05-S2-3.9")]
    public async Task Custody_lives_only_in_custody_warehouses_and_drafts_can_be_cancelled()
    {
        var (depot, _) = await NewDepot();
        var custody = await Send(new SaveWarehouse(null, Unique("CU-"), "Custodie", Database.PloiestiId, WarehouseKind.Custody, ValuationBasis.Cost, null,
            Database.LogisticPartnerId, null, null, 1, null, WarehousePurpose.Regular, null));
        var (_, variants) = await NewItem(Unique("CU"), true, "U");
        var misuse = await Assert.ThrowsAsync<ErpException>(() => Send(new SaveStockDocument(null, StockDocumentTypes.CustodyIn, Today, null, null, null, null,
            depot, null, null, null, null, null, null, [Line(variants[0], "1", "100")], null)));
        Assert.Equal("INV_CUSTODY_MISUSE", misuse.Code);

        var draft = await Send(new SaveStockDocument(null, StockDocumentTypes.CustodyIn, Today, null, null, null, null, custody.Id, null, null, null, null,
            null, null, [Line(variants[0], "4", "250")], null));
        await Send(new ValidateStockDocument(draft.Id, draft.RowVersion));
        Assert.Equal((4m, 1000m), await Stock(custody.Id, variants[0]));

        var other = await Send(new SaveStockDocument(null, StockDocumentTypes.CustodyOut, Today, null, null, custody.Id, null, null, null, null, null, null,
            null, null, [Line(variants[0], "1")], null));
        var cancelled = await Send(new CancelStockDocument(other.Id, other.RowVersion));
        Assert.Equal((byte)4, (await Send(new GetStockDocument(cancelled.Id))).Status);
        var notDraft = await Assert.ThrowsAsync<ErpException>(() => Send(new ValidateStockDocument(other.Id, cancelled.RowVersion)));
        Assert.Equal("INV_DOCUMENT_NOT_DRAFT", notDraft.Code);
    }
}
