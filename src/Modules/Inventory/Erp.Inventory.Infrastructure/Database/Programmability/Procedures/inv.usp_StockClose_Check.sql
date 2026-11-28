CREATE OR ALTER PROCEDURE inv.usp_StockClose_Check
    @CompanyId int,
    @Code      varchar(40),
    @Start     date,
    @End       date
AS
BEGIN
    -- Pasii Stocurilor din lista de inchidere a lunii (02.05, S3 3.5, 3.7, S1 3.6; C3 3.3): fiecare intoarce problemele gasite (mesaj, document,
    -- suma); fara probleme, pasul e in regula.
    --   COST_RECALC_PENDING: chei in coada de recalculare cu data de la care se reface pana la sfarsitul lunii (S3-A9, 54308);
    --   NEGATIVE_STOCK: chei cu sold negativ la sfarsitul lunii, din soldurile pe zile;
    --   RETAIL_DISCHARGE: gestiunile la pret cu amanuntul cu metoda K (pe gestiune sau firma) cu miscari pana la sfarsitul lunii, fara DA-ul lunii.
    SET NOCOUNT ON;

    IF @Code = 'COST_RECALC_PENDING'
        SELECT CONCAT(N'Gestiunea ', w.Code, N' · ', v.Code, N': costul trebuie refăcut de la ', CONVERT(char(10), q.FromDate, 104), N'.') AS Message,
            CAST(q.ItemVariantId AS nvarchar(100)) AS DocumentRef, CAST(NULL AS decimal(19,2)) AS Amount
        FROM inv.CostRecalcQueue AS q
        JOIN inv.Warehouse AS w ON w.Id = q.WarehouseId
        JOIN core.ItemVariant AS v ON v.Id = q.ItemVariantId
        WHERE q.CompanyId = @CompanyId AND q.FromDate <= @End
        ORDER BY w.Code, v.Code;
    ELSE IF @Code = 'NEGATIVE_STOCK'
        SELECT CONCAT(N'Gestiunea ', w.Code, N' · ', v.Code, N': sold ', FORMAT(b.Balance, N'0.######', 'ro-RO'), N' la ', CONVERT(char(10), @End, 104), N'.')
                AS Message,
            CAST(b.ItemVariantId AS nvarchar(100)) AS DocumentRef, CAST(b.Balance AS decimal(19,2)) AS Amount
        FROM (SELECT d.WarehouseId, d.ItemVariantId, d.DepotKey, d.LotKey, SUM(d.QtyIn - d.QtyOut) AS Balance
              FROM inv.StockDayBalance AS d
              JOIN inv.Warehouse AS x ON x.Id = d.WarehouseId AND x.CompanyId = @CompanyId
              WHERE d.BalanceDate <= @End
              GROUP BY d.WarehouseId, d.ItemVariantId, d.DepotKey, d.LotKey
              HAVING SUM(d.QtyIn - d.QtyOut) < 0) AS b
        JOIN inv.Warehouse AS w ON w.Id = b.WarehouseId
        JOIN core.ItemVariant AS v ON v.Id = b.ItemVariantId
        ORDER BY w.Code, v.Code;
    ELSE IF @Code = 'RETAIL_DISCHARGE'
        SELECT CONCAT(N'Gestiunea ', w.Code, N' nu are descărcarea la preț cu amănuntul (DA) pentru luna ', CONVERT(char(7), @Start, 126), N'.') AS Message,
            CAST(w.Id AS nvarchar(100)) AS DocumentRef, CAST(NULL AS decimal(19,2)) AS Amount
        FROM inv.Warehouse AS w
        LEFT JOIN inv.StockSettings AS ss ON ss.CompanyId = w.CompanyId
        WHERE w.CompanyId = @CompanyId AND w.ValuationBasis = 2 AND COALESCE(w.RetailDischargeMethod, ss.RetailDischargeMethod, 1) = 1
          AND EXISTS (SELECT 1 FROM inv.StockMovement AS m WHERE m.WarehouseId = w.Id AND m.MovementDate <= @End AND m.RetailAmount IS NOT NULL)
          AND NOT EXISTS (SELECT 1 FROM inv.RetailDischarge AS d WHERE d.WarehouseId = w.Id AND d.PeriodStart = @Start)
        ORDER BY w.Code;
END;
