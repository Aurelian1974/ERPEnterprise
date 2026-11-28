using Erp.Inventory.Contracts;

namespace Erp.Inventory.Domain;

/// <summary>Setarile modelului pe firma (inv.ItemStockSettings); null cand randul lipseste (se folosesc valorile implicite ale firmei).</summary>
public sealed record CompanyItemLayer(ValuationMethod Method, TrackingMode Tracking, decimal? MinQty, decimal? MaxQty, ReplenishmentMethod Replenishment,
    bool IsBlockedForIssue);

/// <summary>Un rand de praguri pe gestiune (inv.ItemWarehouseSettings), pe model sau pe varianta.</summary>
public sealed record WarehouseItemLayer(decimal? MinQty, decimal? MaxQty, decimal? ReorderQty, ReplenishmentMethod? Replenishment,
    bool IsBlockedForIssue, bool IsBlockedForReceipt);

/// <summary>Gestiunea pentru care se rezolva politica: tipul, politica ei de stoc negativ si metoda implicita a firmei.</summary>
public sealed record WarehousePolicyLayer(int WarehouseId, WarehouseKind Kind, NegativeStockPolicy NegativeStock, ValuationMethod DefaultMethod);

/// <summary>Precedenta politicii de stoc (02.05, S1 3.4 si 3.6), fara acces la date (NET-01).</summary>
public static class StockPolicyRules
{
    /// <summary>
    /// Pragurile si blocarea vin din primul strat existent: varianta in gestiune → model in gestiune → firma (S1-A8). Blocarea iesirilor pe tot
    /// modelul, pusa pe firma (retragere de pe piata), se aplica oricum. Metoda si urmarirea sunt ale modelului pe firma. Variantele urmarite pe
    /// lot sau serie, gestiunile de tranzit si de custodie au mereu stocul negativ blocat (S1 3.6).
    /// </summary>
    public static ItemStockPolicyDto Resolve(WarehousePolicyLayer warehouse, int itemId, int itemVariantId, CompanyItemLayer? company,
        WarehouseItemLayer? itemInWarehouse, WarehouseItemLayer? variantInWarehouse)
    {
        var method = company?.Method ?? warehouse.DefaultMethod;
        var tracking = company?.Tracking ?? TrackingMode.None;
        var local = variantInWarehouse ?? itemInWarehouse;
        var negative = tracking != TrackingMode.None || warehouse.Kind is WarehouseKind.Transit or WarehouseKind.Custody
            ? NegativeStockPolicy.Blocked : warehouse.NegativeStock;
        var replenishment = local?.Replenishment ?? company?.Replenishment ?? ReplenishmentMethod.None;
        return local is null
            ? new ItemStockPolicyDto(itemId, itemVariantId, warehouse.WarehouseId, method, tracking, negative, company?.IsBlockedForIssue ?? false, false,
                company?.MinQty, company?.MaxQty, null, replenishment)
            : new ItemStockPolicyDto(itemId, itemVariantId, warehouse.WarehouseId, method, tracking, negative,
                local.IsBlockedForIssue || (company?.IsBlockedForIssue ?? false), local.IsBlockedForReceipt, local.MinQty, local.MaxQty, local.ReorderQty,
                replenishment);
    }
}

/// <summary>O perioada a unui gestionar (S1 3.2); ValidTo null = in functie.</summary>
public sealed record KeeperPeriod(int? Id, byte Role, DateOnly ValidFrom, DateOnly? ValidTo);

/// <summary>Regulile gestionarilor (S1 3.2, regula 4 din 2): cel mult un titular la o data.</summary>
public static class KeeperRules
{
    public const byte Holder = 1;

    /// <summary>Doua perioade se suprapun daca au cel putin o zi comuna (capetele incluse).</summary>
    public static bool Overlaps(KeeperPeriod a, KeeperPeriod b) =>
        a.ValidFrom <= (b.ValidTo ?? DateOnly.MaxValue) && b.ValidFrom <= (a.ValidTo ?? DateOnly.MaxValue);

    /// <summary>Titularul nou (sau modificat) se suprapune cu alt titular al gestiunii (54009, S1-A6).</summary>
    public static bool HolderOverlaps(KeeperPeriod candidate, IEnumerable<KeeperPeriod> existing) =>
        candidate.Role == Holder && existing.Any(e => e.Role == Holder && e.Id != candidate.Id && Overlaps(candidate, e));
}
