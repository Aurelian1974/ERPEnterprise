CREATE OR ALTER PROCEDURE inv.usp_ItemStockSettings_Get
    @CompanyId int,
    @ItemId    int
AS
BEGIN
    -- Tab-ul Stoc al fisei articolului pentru firma curenta (02.05, S1 3.4; macheta s1-03): setarile modelului (sau valorile implicite ale firmei,
    -- daca randul lipseste, cu Id NULL), pragurile pe gestiuni, variantele modelului si gestiunile care pot fi implicite. Model nestocabil: 54018.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @ItemId AND IsStockable = 1)
        THROW 54018, N'Articolul nu există sau nu este stocabil.', 1;

    SELECT s.Id, s.DefaultWarehouseId, ISNULL(s.ValuationMethod, ISNULL(c.DefaultValuationMethod, 2)) AS ValuationMethod,
        ISNULL(s.TrackingMode, 0) AS TrackingMode, s.MinQty, s.MaxQty, ISNULL(s.ReplenishmentMethod, 0) AS ReplenishmentMethod,
        ISNULL(s.IsBlockedForIssue, 0) AS IsBlockedForIssue, s.RowVersion
    FROM (SELECT 1 AS One) AS d
    LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = @CompanyId AND s.ItemId = @ItemId
    LEFT JOIN inv.StockSettings AS c ON c.CompanyId = @CompanyId;

    SELECT x.Id, x.WarehouseId, w.Code AS WarehouseCode, w.Name AS WarehouseName, x.ItemVariantId, v.Code AS VariantCode, x.MinQty, x.MaxQty,
        x.ReorderQty, x.ReplenishmentMethod, x.IsBlockedForIssue, x.IsBlockedForReceipt, x.IsListed, x.RowVersion
    FROM inv.ItemWarehouseSettings AS x
    JOIN inv.Warehouse AS w ON w.Id = x.WarehouseId
    LEFT JOIN core.ItemVariant AS v ON v.Id = x.ItemVariantId
    WHERE x.ItemId = @ItemId AND w.CompanyId = @CompanyId
    ORDER BY w.CodeKey, v.CodeKey;

    SELECT Id, Code, Name FROM core.ItemVariant WHERE ItemId = @ItemId AND IsActive = 1 ORDER BY CodeKey;

    SELECT Id, Code, Name FROM inv.Warehouse WHERE CompanyId = @CompanyId AND IsActive = 1 AND Kind IN (1, 2) ORDER BY CodeKey;
END;
