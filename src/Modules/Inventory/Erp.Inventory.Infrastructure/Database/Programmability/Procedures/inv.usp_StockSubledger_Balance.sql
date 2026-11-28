CREATE OR ALTER PROCEDURE inv.usp_StockSubledger_Balance
    @CompanyId int,
    @AsOf      date
AS
BEGIN
    -- Evidenta operativa a stocului pentru concordanta C3 (02.05, S3 3.6), pe gestiune, cu data contabila pana la @AsOf: ce trebuie sa arate,
    -- net, conturile de stoc, adaos (378) si TVA neexigibila (4428) cu dimensiunea gestiunii, daca toate miscarile si ajustarile sunt contate.
    --   gestiunile la cost: valoarea miscarilor plus ajustarile care schimba stocul (InStock = 1), adica valoarea stocului;
    --   gestiunile la pret cu amanuntul: intrarile si iesirile descarcate pe loc (metoda 2, predarile) la cost; iesirile prin K (metoda 1) la pretul
    --   cu amanuntul; plus adaosul si TVA-ul descarcate prin DA (documentele lunilor incheiate pana la data). Modificarile de pret nu schimba netul.
    -- Custodia (bunurile tertilor) nu intra.
    SET NOCOUNT ON;

    WITH Movements AS (
        SELECT m.WarehouseId,
            SUM(CASE WHEN m.RetailAmount IS NOT NULL AND t.Direction = -1 AND ISNULL(m.RetailDischargeMethod, 1) = 1 THEN m.RetailAmount ELSE m.CostAmount END)
                AS Amount
        FROM inv.StockMovement AS m
        JOIN inv.MovementType AS t ON t.Id = m.MovementTypeId
        WHERE m.CompanyId = @CompanyId AND m.AccountingDate <= @AsOf AND t.Code NOT IN ('CUSTODY_IN', 'CUSTODY_OUT')
        GROUP BY m.WarehouseId
    ),
    Adjustments AS (
        SELECT a.WarehouseId, SUM(a.Amount) AS Amount
        FROM inv.CostAdjustment AS a
        WHERE a.CompanyId = @CompanyId AND a.InStock = 1 AND a.AccountingDate <= @AsOf
        GROUP BY a.WarehouseId
    ),
    Discharges AS (
        SELECT d.WarehouseId, SUM(d.Markup + d.SalesVat) AS Amount
        FROM inv.RetailDischarge AS d
        WHERE d.CompanyId = @CompanyId AND EOMONTH(d.PeriodStart) <= @AsOf
        GROUP BY d.WarehouseId
    )
    SELECT w.Id AS KeyId, CONCAT(w.Code, N' · ', w.Name) AS KeyName,
        CAST(ISNULL(m.Amount, 0) + ISNULL(a.Amount, 0) + ISNULL(d.Amount, 0) AS decimal(19,2)) AS Amount
    FROM inv.Warehouse AS w
    LEFT JOIN Movements AS m ON m.WarehouseId = w.Id
    LEFT JOIN Adjustments AS a ON a.WarehouseId = w.Id
    LEFT JOIN Discharges AS d ON d.WarehouseId = w.Id
    WHERE w.CompanyId = @CompanyId AND w.Kind <> 4 AND (m.WarehouseId IS NOT NULL OR a.WarehouseId IS NOT NULL OR d.WarehouseId IS NOT NULL);
END;
