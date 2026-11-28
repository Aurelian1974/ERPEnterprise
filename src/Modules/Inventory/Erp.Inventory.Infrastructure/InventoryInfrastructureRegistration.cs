using Erp.Core.Contracts;
using Erp.Inventory.Application.Settings;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Infrastructure.Warehouses;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Inventory.Infrastructure;

public static class InventoryInfrastructureRegistration
{
    /// <summary>Accesul la date al Stocurilor si codurile de eroare SQL ale modulului (54000–54999). Conexiunea si tranzactia sunt ale Nucleului.</summary>
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ISqlErrorMap, InventorySqlErrors>();
        services.AddScoped<IWarehouseStore, WarehouseStore>();
        services.AddScoped<IStockSettingsStore, StockSettingsStore>();
        services.AddScoped<IStockCompanySetup, StockCompanySetup>();
        services.AddScoped<IWarehouseKeeperStore, WarehouseKeeperStore>();
        services.AddScoped<Application.Items.IItemStockSettingsStore, Items.ItemStockSettingsStore>();
        services.AddScoped<Contracts.IStockPolicyProvider, Items.StockPolicyProvider>();
        return services;
    }
}

/// <summary>
/// Codurile THROW ale Stocurilor (02.05, S1 7): plaja 54000–54999, impartita pe parti (S1 54000–54099, S2 54100–54299, S3 54300–54449,
/// S4 54450–54549, S5 54550–54649, S6 54650–54799, S7 54800–54899). 54018–54021 sunt adaugate la implementare.
/// </summary>
public sealed class InventorySqlErrors : ISqlErrorMap
{
    public IReadOnlyDictionary<int, (ErpErrorKind Kind, string Code)> Errors { get; } = new Dictionary<int, (ErpErrorKind, string)>
    {
        [54001] = (ErpErrorKind.NotFound, "INV_WAREHOUSE_NOT_FOUND"),
        [54002] = (ErpErrorKind.Conflict, "INV_WAREHOUSE_CODE_EXISTS"),
        [54003] = (ErpErrorKind.Validation, "INV_WAREHOUSE_LOCATION_INVALID"),
        [54004] = (ErpErrorKind.Conflict, "INV_WAREHOUSE_LOCKED_FIELD"),
        [54005] = (ErpErrorKind.Conflict, "INV_WAREHOUSE_BLOCKED"),
        [54006] = (ErpErrorKind.Conflict, "INV_WAREHOUSE_NOT_EMPTY"),
        [54007] = (ErpErrorKind.Validation, "INV_WAREHOUSE_RETAIL_PRICELIST_REQUIRED"),
        [54008] = (ErpErrorKind.Validation, "INV_RETAIL_PRICE_MISSING"),
        [54009] = (ErpErrorKind.Conflict, "INV_KEEPER_OVERLAP"),
        [54010] = (ErpErrorKind.Conflict, "INV_TRACKING_CHANGE_WITH_STOCK"),
        [54011] = (ErpErrorKind.Conflict, "INV_VALUATION_CHANGE_INVALID"),
        [54012] = (ErpErrorKind.Validation, "INV_NEGATIVE_POLICY_NOT_ALLOWED"),
        [54013] = (ErpErrorKind.Conflict, "INV_HANDOVER_COUNT_REQUIRED"),
        [54014] = (ErpErrorKind.Conflict, "INV_CONCURRENCY_CONFLICT"),
        [54015] = (ErpErrorKind.Validation, "INV_SPECIFIC_ID_REQUIRES_TRACKING"),
        [54016] = (ErpErrorKind.Conflict, "INV_STOCK_LEVEL_CHANGE_WITH_STOCK"),
        [54017] = (ErpErrorKind.Validation, "INV_DEPOT_LOCATION_INVALID"),
        [54018] = (ErpErrorKind.Validation, "INV_INVALID_REFERENCE"),
        [54019] = (ErpErrorKind.Conflict, "INV_HANDOVER_IN_PROGRESS"),
        [54020] = (ErpErrorKind.Conflict, "INV_HANDOVER_NOT_OPEN"),
        [54021] = (ErpErrorKind.Conflict, "INV_ITEM_WAREHOUSE_SETTINGS_EXISTS"),
    };
}
