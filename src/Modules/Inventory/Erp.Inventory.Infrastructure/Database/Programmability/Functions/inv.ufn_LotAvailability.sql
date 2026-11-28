CREATE OR ALTER FUNCTION inv.ufn_LotAvailability (@WarehouseId int, @DepotKey int, @ItemVariantId int, @OnDate date)
RETURNS TABLE
AS
RETURN
    -- Disponibilul pe lot al unei chei (02.05, S4 3.5): soldul la data data si minimul soldurilor de la datele ulterioare, din soldurile pe zile,
    -- ca o iesire datata in trecut sa nu lase lotul pe minus mai tarziu.
    WITH Running AS (
        SELECT b.LotKey, b.BalanceDate,
            SUM(b.QtyIn - b.QtyOut) OVER (PARTITION BY b.LotKey ORDER BY b.BalanceDate ROWS UNBOUNDED PRECEDING) AS Balance
        FROM inv.StockDayBalance AS b
        WHERE b.WarehouseId = @WarehouseId AND b.DepotKey = @DepotKey AND b.ItemVariantId = @ItemVariantId AND b.LotKey <> 0
    ),
    Ranked AS (
        SELECT r.LotKey, r.BalanceDate, r.Balance,
            ROW_NUMBER() OVER (PARTITION BY r.LotKey ORDER BY CASE WHEN r.BalanceDate <= @OnDate THEN 0 ELSE 1 END, r.BalanceDate DESC) AS AtDateRank
        FROM Running AS r
    ),
    Lots AS (
        SELECT LotKey,
            MAX(CASE WHEN AtDateRank = 1 AND BalanceDate <= @OnDate THEN Balance END) AS AtDate,
            MIN(CASE WHEN BalanceDate > @OnDate THEN Balance END) AS Later
        FROM Ranked
        GROUP BY LotKey
    )
    SELECT LotKey AS LotId,
        CAST(CASE WHEN Later IS NULL OR ISNULL(AtDate, 0) < Later THEN ISNULL(AtDate, 0) ELSE Later END AS decimal(19,6)) AS Available
    FROM Lots;
