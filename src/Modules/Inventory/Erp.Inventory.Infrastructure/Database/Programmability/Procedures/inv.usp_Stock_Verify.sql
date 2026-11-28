CREATE OR ALTER PROCEDURE inv.usp_Stock_Verify
    @CompanyId   int,
    @WarehouseId int = NULL
AS
BEGIN
    -- Diagnosticul soldului (02.05, S2 7, S2-A1; S3-A1): cheile la care soldul curent sau soldurile pe zile difera de agregarea registrului.
    -- Niciun rand = soldurile bat cu registrul.
    SET NOCOUNT ON;

    -- Valoarea = suma miscarilor + ajustarile care schimba stocul (S3 regula 2, S3-A1).
    WITH Ledger AS (
        SELECT WarehouseId, DepotKey, ItemVariantId, LotKey, SUM(Qty) AS Qty, SUM(Cost) AS Cost
        FROM (SELECT WarehouseId, ISNULL(DepotId, 0) AS DepotKey, ItemVariantId, ISNULL(LotId, 0) AS LotKey, QtyBase AS Qty, CostAmount AS Cost
              FROM inv.StockMovement WHERE CompanyId = @CompanyId AND (@WarehouseId IS NULL OR WarehouseId = @WarehouseId)
              UNION ALL
              SELECT WarehouseId, ISNULL(DepotId, 0), ItemVariantId, ISNULL(LotId, 0), 0, Amount
              FROM inv.CostAdjustment WHERE CompanyId = @CompanyId AND InStock = 1 AND (@WarehouseId IS NULL OR WarehouseId = @WarehouseId)) AS x
        GROUP BY WarehouseId, DepotKey, ItemVariantId, LotKey
    ),
    Days AS (
        SELECT b.WarehouseId, b.DepotKey, b.ItemVariantId, b.LotKey, SUM(b.QtyIn - b.QtyOut) AS Qty
        FROM inv.StockDayBalance AS b JOIN inv.Warehouse AS w ON w.Id = b.WarehouseId
        WHERE w.CompanyId = @CompanyId AND (@WarehouseId IS NULL OR b.WarehouseId = @WarehouseId)
        GROUP BY b.WarehouseId, b.DepotKey, b.ItemVariantId, b.LotKey
    ),
    DayRows AS (
        SELECT WarehouseId, ISNULL(DepotId, 0) AS DepotKey, ItemVariantId, ISNULL(LotId, 0) AS LotKey, MovementDate,
            SUM(CASE WHEN QtyBase > 0 THEN QtyBase ELSE 0 END) AS QtyIn, SUM(CASE WHEN QtyBase < 0 THEN -QtyBase ELSE 0 END) AS QtyOut
        FROM inv.StockMovement
        WHERE CompanyId = @CompanyId AND (@WarehouseId IS NULL OR WarehouseId = @WarehouseId)
        GROUP BY WarehouseId, ISNULL(DepotId, 0), ItemVariantId, ISNULL(LotId, 0), MovementDate
    )
    SELECT COALESCE(l.WarehouseId, s.WarehouseId) AS WarehouseId, COALESCE(l.ItemVariantId, s.ItemVariantId) AS ItemVariantId,
        ISNULL(l.Qty, 0) AS LedgerQty, ISNULL(s.QtyOnHand, 0) AS StockQty, ISNULL(l.Cost, 0) AS LedgerCost, ISNULL(s.CostOnHand, 0) AS StockCost,
        ISNULL(d.Qty, 0) AS DayQty, N'Sold' AS Kind
    FROM Ledger AS l
    FULL JOIN (SELECT * FROM inv.Stock WHERE CompanyId = @CompanyId AND (@WarehouseId IS NULL OR WarehouseId = @WarehouseId)) AS s
        ON s.WarehouseId = l.WarehouseId AND s.DepotKey = l.DepotKey AND s.ItemVariantId = l.ItemVariantId AND s.LotKey = l.LotKey
    LEFT JOIN Days AS d ON d.WarehouseId = COALESCE(l.WarehouseId, s.WarehouseId) AND d.DepotKey = COALESCE(l.DepotKey, s.DepotKey)
        AND d.ItemVariantId = COALESCE(l.ItemVariantId, s.ItemVariantId) AND d.LotKey = COALESCE(l.LotKey, s.LotKey)
    WHERE ISNULL(l.Qty, 0) <> ISNULL(s.QtyOnHand, 0) OR ISNULL(l.Cost, 0) <> ISNULL(s.CostOnHand, 0) OR ISNULL(l.Qty, 0) <> ISNULL(d.Qty, 0)
    UNION ALL
    SELECT r.WarehouseId, r.ItemVariantId, r.QtyIn, ISNULL(b.QtyIn, 0), r.QtyOut, ISNULL(b.QtyOut, 0), 0, N'Zi'
    FROM DayRows AS r
    LEFT JOIN inv.StockDayBalance AS b ON b.WarehouseId = r.WarehouseId AND b.DepotKey = r.DepotKey AND b.ItemVariantId = r.ItemVariantId
        AND b.LotKey = r.LotKey AND b.BalanceDate = r.MovementDate
    WHERE r.QtyIn <> ISNULL(b.QtyIn, 0) OR r.QtyOut <> ISNULL(b.QtyOut, 0);
END;
