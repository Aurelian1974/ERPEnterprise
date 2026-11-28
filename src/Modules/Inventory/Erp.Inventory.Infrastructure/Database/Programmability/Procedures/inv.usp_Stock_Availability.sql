CREATE OR ALTER PROCEDURE inv.usp_Stock_Availability
    @CompanyId   int,
    @DepotId     int = NULL,
    @WarehouseId int = NULL,
    @Variants    inv.IdList READONLY,
    @OnDate      date
AS
BEGIN
    -- Stocul unui set de variante la o data (02.05, S2 6; IStockLedger.GetAvailabilityAsync), pe gestiune si depozit, din soldurile pe zile.
    -- Rezervatul vine cu S5; pana atunci disponibilul e egal cu stocul.
    SET NOCOUNT ON;

    SELECT b.ItemVariantId, b.WarehouseId, NULLIF(b.DepotKey, 0) AS DepotId, SUM(b.QtyIn - b.QtyOut) AS OnHand, CAST(0 AS decimal(19,6)) AS Reserved,
        SUM(b.QtyIn - b.QtyOut) AS Available
    FROM inv.StockDayBalance AS b
    JOIN inv.Warehouse AS w ON w.Id = b.WarehouseId
    WHERE w.CompanyId = @CompanyId AND b.BalanceDate <= @OnDate AND b.ItemVariantId IN (SELECT Id FROM @Variants)
      AND (@WarehouseId IS NULL OR b.WarehouseId = @WarehouseId) AND (@DepotId IS NULL OR b.DepotKey = @DepotId)
    GROUP BY b.ItemVariantId, b.WarehouseId, b.DepotKey
    HAVING SUM(b.QtyIn - b.QtyOut) <> 0
    ORDER BY b.ItemVariantId, b.WarehouseId, b.DepotKey;
END;
