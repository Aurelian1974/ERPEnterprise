namespace Erp.Inventory.Application;

/// <summary>Codul modulului in licenta (08, 4.3) si in [Module].</summary>
public static class InventoryModule
{
    public const string Code = "Inventory";
}

/// <summary>
/// Permisiunile Stocurilor (02.05, S1 8); aceleasi coduri ca in seed-ul inv (core.Permission, ModuleCode „Inv”). Prefixul „Inv.” e cel din
/// specificatia validata; codul modulului din licenta ramane „Inventory”.
/// </summary>
public static class InventoryPermissions
{
    public const string WarehouseView = "Inv.Warehouse.View";
    public const string WarehouseManage = "Inv.Warehouse.Manage";
    public const string WarehouseBlock = "Inv.Warehouse.Block";
    public const string WarehouseKeeper = "Inv.Warehouse.Keeper";
    public const string SettingsManage = "Inv.Settings.Manage";
    public const string ItemSettingsManage = "Inv.ItemSettings.Manage";
    public const string ValuationMethodChange = "Inv.ValuationMethod.Change";
    public const string DocumentView = "Inv.Document.View";
    public const string TransferEdit = "Inv.Transfer.Edit";
    public const string TransferValidate = "Inv.Transfer.Validate";
    public const string LoadUnloadEdit = "Inv.LoadUnload.Edit";
    public const string LoadUnloadValidate = "Inv.LoadUnload.Validate";
    public const string LoadUnloadApprove = "Inv.LoadUnload.Approve";
    public const string ConsumptionEdit = "Inv.Consumption.Edit";
    public const string ConsumptionValidate = "Inv.Consumption.Validate";
    public const string ProductionNoteEdit = "Inv.ProductionNote.Edit";
    public const string ProductionNoteValidate = "Inv.ProductionNote.Validate";
    public const string CustodyEdit = "Inv.Custody.Edit";
    public const string CustodyValidate = "Inv.Custody.Validate";
    public const string DocumentReverse = "Inv.Document.Reverse";
    public const string MovementBackdate = "Inv.Movement.Backdate";
    public const string ReasonManage = "Inv.Reason.Manage";
    public const string CostView = "Inv.Cost.View";
    public const string CostRecalculate = "Inv.Cost.Recalculate";
    public const string LandedCostEdit = "Inv.LandedCost.Edit";
    public const string LandedCostValidate = "Inv.LandedCost.Validate";
    public const string RetailPriceEdit = "Inv.RetailPrice.Edit";
    public const string RetailPriceValidate = "Inv.RetailPrice.Validate";
    public const string RetailDischargeValidate = "Inv.RetailDischarge.Validate";
    public const string StandardCostUpdate = "Inv.StandardCost.Update";
    public const string LotView = "Inv.Lot.View";
    public const string LotEdit = "Inv.Lot.Edit";
    public const string LotRelease = "Inv.Lot.Release";
    public const string LotBlock = "Inv.Lot.Block";
    public const string LotOverrideShelfLife = "Inv.Lot.OverrideShelfLife";
    public const string LotSellAfterBestBefore = "Inv.Lot.SellAfterBestBefore";
    public const string LotReceiveExpired = "Inv.Lot.ReceiveExpired";
    public const string SerialEditIdentifiers = "Inv.Serial.EditIdentifiers";
    public const string SettingsSerialIdentifiers = "Inv.Settings.SerialIdentifiers";
    public const string LotChangeEdit = "Inv.LotChange.Edit";
    public const string LotChangeValidate = "Inv.LotChange.Validate";
    public const string RecallEdit = "Inv.Recall.Edit";
    public const string RecallValidate = "Inv.Recall.Validate";
    public const string RecallClose = "Inv.Recall.Close";
}

/// <summary>Codurile API ale Stocurilor ridicate din C# (NET-09). Cele din SQL sunt in InventorySqlErrors.</summary>
public static class InventoryErrorCodes
{
    public const string WarehouseNotFound = "INV_WAREHOUSE_NOT_FOUND";
    public const string BackdateNotAllowed = "INV_BACKDATE_NOT_ALLOWED";
}

/// <summary>Rezultatul unei salvari: identificatorul si versiunea noua (SQL-19).</summary>
public sealed record InventorySaved(int Id, byte[] RowVersion);

/// <summary>O intrare din istoricul unei fise a Stocurilor (jurnalul de audit, 02.01 fluxul 4.5).</summary>
public sealed record InventoryHistoryEntryDto(long Id, DateTime OccurredAt, string Action, string EntityType, long? EntityId, string UserName, string? Changes);

/// <summary>Assembly-ul cazurilor de utilizare, dat pipeline-ului comun (AddErpMediator) de gazda.</summary>
public static class InventoryApplication
{
    public static System.Reflection.Assembly Assembly => typeof(InventoryApplication).Assembly;
}
