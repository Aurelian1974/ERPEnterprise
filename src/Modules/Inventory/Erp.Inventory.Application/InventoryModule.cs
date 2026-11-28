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
}

/// <summary>Codurile API ale Stocurilor ridicate din C# (NET-09). Cele din SQL sunt in InventorySqlErrors.</summary>
public static class InventoryErrorCodes
{
    public const string WarehouseNotFound = "INV_WAREHOUSE_NOT_FOUND";
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
