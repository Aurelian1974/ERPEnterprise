CREATE OR ALTER PROCEDURE inv.usp_StockPolicy_Layers
    @CompanyId   int,
    @WarehouseId int,
    @Variants    inv.IdList READONLY
AS
BEGIN
    -- Straturile politicii de stoc pentru un set de variante intr-o gestiune (02.05, S1 3.4, 3.6; IStockPolicyProvider, un singur apel cu TVP,
    -- SP-07): gestiunea, valorile implicite ale firmei, setarile modelului pe firma si randurile pe gestiune (pe model si pe varianta).
    -- Precedenta (varianta in gestiune → model in gestiune → firma) se aplica in C# (StockPolicyRules). Gestiunea altei firme: 54001.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id = @WarehouseId AND CompanyId = @CompanyId)
        THROW 54001, N'Gestiunea nu există în firma curentă.', 1;

    SELECT w.Kind, w.NegativeStockPolicy, ISNULL(c.DefaultValuationMethod, 2) AS DefaultValuationMethod
    FROM inv.Warehouse AS w
    LEFT JOIN inv.StockSettings AS c ON c.CompanyId = w.CompanyId
    WHERE w.Id = @WarehouseId;

    SELECT v.Id AS ItemVariantId, v.ItemId, s.ValuationMethod, s.TrackingMode, s.MinQty, s.MaxQty, s.ReplenishmentMethod, s.IsBlockedForIssue
    FROM @Variants AS t
    JOIN core.ItemVariant AS v ON v.Id = t.Id
    LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = @CompanyId AND s.ItemId = v.ItemId;

    SELECT x.ItemId, x.ItemVariantId, x.MinQty, x.MaxQty, x.ReorderQty, x.ReplenishmentMethod, x.IsBlockedForIssue, x.IsBlockedForReceipt
    FROM inv.ItemWarehouseSettings AS x
    WHERE x.WarehouseId = @WarehouseId
      AND (x.ItemVariantId IN (SELECT Id FROM @Variants)
           OR (x.ItemVariantId IS NULL AND x.ItemId IN (SELECT v.ItemId FROM @Variants AS t JOIN core.ItemVariant AS v ON v.Id = t.Id)));
END;
