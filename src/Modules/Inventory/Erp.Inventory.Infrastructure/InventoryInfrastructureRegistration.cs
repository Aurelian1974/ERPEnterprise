using Erp.Core.Contracts;
using Erp.Inventory.Application.Settings;
using Erp.Inventory.Application.Warehouses;
using Erp.Inventory.Infrastructure.Warehouses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddScoped<IDepotStore, DepotStore>();
        services.AddScoped<Contracts.IWarehouseLookup, WarehouseLookup>();
        services.AddScoped<Application.Movements.IStockMovementStore, Movements.StockMovementStore>();
        services.AddScoped<Application.Movements.StockLedger>();
        services.AddScoped<Application.Movements.ICostRecalcStore, Movements.CostRecalcStore>();
        services.AddScoped<Application.Movements.CostRecalculator>();
        services.AddTransient<Movements.CostRecalcJob>();
        services.AddScoped<Application.Costing.ILandedCostStore, Costing.LandedCostStore>();
        services.AddScoped<Application.Costing.LandedCostService>();
        services.AddScoped<Application.Documents.IStockDocumentStore, Documents.StockDocumentStore>();
        services.AddScoped<Application.Documents.TransferService>();
        services.AddScoped<Application.Movements.IStockMovementQueries, Movements.StockMovementQueries>();
        services.AddScoped<Application.Movements.IStockVariantSearch, Movements.StockVariantSearch>();
        // Portul de stoc al Nucleului (02.05, S2 6): Stocurile inlocuiesc implementarea goala.
        services.Replace(ServiceDescriptor.Scoped<IStockLedger>(sp => sp.GetRequiredService<Application.Movements.StockLedger>()));
        services.AddScoped<Application.Items.IItemStockSettingsStore, Items.ItemStockSettingsStore>();
        services.AddScoped<Contracts.IStockPolicyProvider, Items.StockPolicyProvider>();
        return services;
    }
}

/// <summary>
/// Codurile THROW ale Stocurilor (02.05, S1 7): plaja 54000–54999, impartita pe parti (S1 54000–54099, S2 54100–54299, S3 54300–54449,
/// S4 54450–54549, S5 54550–54649, S6 54650–54799, S7 54800–54899). 54018–54023 si 54118–54119 sunt adaugate la implementare.
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
        [54022] = (ErpErrorKind.Conflict, "INV_DEPOT_CODE_EXISTS"),
        [54023] = (ErpErrorKind.NotFound, "INV_DEPOT_NOT_FOUND"),
        [54101] = (ErpErrorKind.Conflict, "INV_STOCK_INSUFFICIENT"),
        [54102] = (ErpErrorKind.Conflict, "INV_SOURCE_ALREADY_POSTED"),
        [54103] = (ErpErrorKind.Forbidden, "INV_BACKDATE_NOT_ALLOWED"),
        [54104] = (ErpErrorKind.Validation, "INV_LINE_QTY_INVALID"),
        [54105] = (ErpErrorKind.Validation, "INV_VARIANT_NOT_STOCKABLE"),
        [54106] = (ErpErrorKind.Validation, "INV_WAREHOUSE_PAIR_INVALID"),
        [54107] = (ErpErrorKind.Conflict, "INV_TRANSFER_MODE_MISMATCH"),
        [54108] = (ErpErrorKind.Validation, "INV_TRANSIT_DIRECT_USE"),
        [54109] = (ErpErrorKind.Validation, "INV_RECEIPT_EXCEEDS_SHIPMENT"),
        [54110] = (ErpErrorKind.Validation, "INV_REASON_REQUIRED"),
        [54111] = (ErpErrorKind.Validation, "INV_ATTACHMENT_REQUIRED"),
        [54112] = (ErpErrorKind.Conflict, "INV_APPROVAL_REQUIRED"),
        [54113] = (ErpErrorKind.Forbidden, "INV_APPROVER_IS_AUTHOR"),
        [54114] = (ErpErrorKind.Conflict, "INV_REVERSAL_INVALID"),
        [54115] = (ErpErrorKind.Validation, "INV_COST_CENTER_REQUIRED"),
        [54116] = (ErpErrorKind.Validation, "INV_CUSTODY_MISUSE"),
        [54117] = (ErpErrorKind.Conflict, "INV_DOCUMENT_CONCURRENCY"),
        [54118] = (ErpErrorKind.Conflict, "INV_DOCUMENT_NOT_DRAFT"),
        [54119] = (ErpErrorKind.Validation, "INV_UNIT_COST_REQUIRED"),
        [54199] = (ErpErrorKind.Conflict, "INV_MOVEMENT_IMMUTABLE"),
        [54302] = (ErpErrorKind.Conflict, "INV_COST_REVERSAL_LAYER_CONSUMED"),
        [54303] = (ErpErrorKind.Validation, "INV_LANDED_COST_TARGET_INVALID"),
        [54304] = (ErpErrorKind.Validation, "INV_LANDED_COST_ALLOCATION_MISMATCH"),
        [54308] = (ErpErrorKind.Conflict, "INV_RECALC_PENDING"),
    };
}
