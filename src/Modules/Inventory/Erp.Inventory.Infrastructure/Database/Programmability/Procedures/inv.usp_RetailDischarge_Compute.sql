CREATE OR ALTER PROCEDURE inv.usp_RetailDischarge_Compute
    @CompanyId   int,
    @WarehouseId int,
    @PeriodStart date
AS
BEGIN
    -- Datele descarcarii lunare la pret cu amanuntul (02.05, S3 3.7, metoda 1), din miscarile gestiunii, documentele MP si descarcarile anterioare
    -- (nu din contabilitate): soldurile initiale si rulajele lunii pentru 371, 378 si 4428, vanzarile cu amanuntul si TVA-ul lor. Intrarile sunt
    -- miscarile al caror tip are sensul +1 (stornarile lor scad), iesirile cele cu sensul -1. Gestiunea trebuie sa fie la pret cu amanuntul
    -- (54312), cu descarcarea prin K (54314). Iesirile descarcate pe loc (metoda 2, predarile) scad adaosul si TVA-ul (Rd 378, Rd 4428) si nu
    -- intra in vanzarile descarcate prin K. Calculul lui K il face apelantul (RetailDischargeCalculator).
    SET NOCOUNT ON;

    DECLARE @Basis tinyint, @Method tinyint;
    SELECT @Basis = w.ValuationBasis, @Method = COALESCE(w.RetailDischargeMethod, ss.RetailDischargeMethod, 1)
    FROM inv.Warehouse AS w LEFT JOIN inv.StockSettings AS ss ON ss.CompanyId = w.CompanyId
    WHERE w.Id = @WarehouseId AND w.CompanyId = @CompanyId;
    IF @Basis IS NULL OR @Basis <> 2
        THROW 54312, N'Descărcarea prin K se face doar pentru o gestiune la preț cu amănuntul a firmei.', 1;
    IF @Method <> 1
        THROW 54314, N'Gestiunea descarcă costul pe fiecare vânzare (metoda 2); nu se face documentul DA.', 1;

    DECLARE @End date = DATEADD(MONTH, 1, @PeriodStart);

    WITH M AS (
        SELECT CASE WHEN m.MovementDate < @PeriodStart THEN 0 ELSE 1 END AS InPeriod, t.Direction AS TypeDirection, m.RetailAmount,
            ISNULL(m.RetailVatAmount, 0) AS RetailVat, m.CostAmount, ISNULL(m.RetailDischargeMethod, 1) AS Method
        FROM inv.StockMovement AS m
        JOIN inv.MovementType AS t ON t.Id = m.MovementTypeId
        WHERE m.WarehouseId = @WarehouseId AND m.RetailAmount IS NOT NULL AND m.MovementDate < @End
    ),
    P AS (
        SELECT CASE WHEN d.DocumentDate < @PeriodStart THEN 0 ELSE 1 END AS InPeriod, ISNULL(d.DiffAmount, 0) AS Diff, ISNULL(d.DiffVatAmount, 0) AS DiffVat
        FROM inv.RetailPriceChange AS d
        WHERE d.WarehouseId = @WarehouseId AND d.Status = 2 AND d.DocumentDate < @End
    ),
    D AS (
        SELECT SUM(Markup) AS Markup, SUM(SalesVat) AS SalesVat FROM inv.RetailDischarge WHERE WarehouseId = @WarehouseId AND PeriodStart < @PeriodStart
    )
    SELECT
        CAST(ISNULL((SELECT SUM(RetailAmount) FROM M WHERE InPeriod = 0), 0) + ISNULL((SELECT SUM(Diff) FROM P WHERE InPeriod = 0), 0) AS decimal(19,2))
            AS OpeningRetail,
        CAST(ISNULL((SELECT SUM(RetailAmount) FROM M WHERE InPeriod = 1 AND TypeDirection = 1), 0) + ISNULL((SELECT SUM(Diff) FROM P WHERE InPeriod = 1), 0)
            + ISNULL((SELECT SUM(RetailAmount) FROM M WHERE InPeriod = 1 AND TypeDirection = -1 AND Method = 2), 0) AS decimal(19,2)) AS RetailIn,
        CAST(ISNULL((SELECT -SUM(RetailAmount) FROM M WHERE InPeriod = 1 AND TypeDirection = -1 AND Method = 1), 0) AS decimal(19,2)) AS RetailOut,
        CAST(ISNULL((SELECT SUM(RetailAmount - RetailVat - CostAmount) FROM M WHERE InPeriod = 0 AND TypeDirection = 1), 0)
            + ISNULL((SELECT SUM(Diff - DiffVat) FROM P WHERE InPeriod = 0), 0) - ISNULL((SELECT Markup FROM D), 0)
            + ISNULL((SELECT SUM(RetailAmount - RetailVat - CostAmount) FROM M WHERE InPeriod = 0 AND TypeDirection = -1 AND Method = 2), 0)
            AS decimal(19,2)) AS OpeningMarkup,
        CAST(ISNULL((SELECT SUM(RetailAmount - RetailVat - CostAmount) FROM M WHERE InPeriod = 1 AND TypeDirection = 1), 0)
            + ISNULL((SELECT SUM(Diff - DiffVat) FROM P WHERE InPeriod = 1), 0)
            + ISNULL((SELECT SUM(RetailAmount - RetailVat - CostAmount) FROM M WHERE InPeriod = 1 AND TypeDirection = -1 AND Method = 2), 0)
            AS decimal(19,2)) AS MarkupIn,
        CAST(ISNULL((SELECT SUM(RetailVat) FROM M WHERE InPeriod = 0 AND TypeDirection = 1), 0) + ISNULL((SELECT SUM(DiffVat) FROM P WHERE InPeriod = 0), 0)
            - ISNULL((SELECT SalesVat FROM D), 0) + ISNULL((SELECT SUM(RetailVat) FROM M WHERE InPeriod = 0 AND TypeDirection = -1 AND Method = 2), 0)
            AS decimal(19,2)) AS OpeningVat,
        CAST(ISNULL((SELECT SUM(RetailVat) FROM M WHERE InPeriod = 1 AND TypeDirection = 1), 0) + ISNULL((SELECT SUM(DiffVat) FROM P WHERE InPeriod = 1), 0)
            + ISNULL((SELECT SUM(RetailVat) FROM M WHERE InPeriod = 1 AND TypeDirection = -1 AND Method = 2), 0) AS decimal(19,2)) AS VatIn,
        CAST(ISNULL((SELECT -SUM(RetailVat) FROM M WHERE InPeriod = 1 AND TypeDirection = -1 AND Method = 1), 0) AS decimal(19,2)) AS SalesVat,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM inv.RetailDischarge WHERE WarehouseId = @WarehouseId AND PeriodStart = @PeriodStart) THEN 1 ELSE 0 END AS bit)
            AS AlreadyDischarged,
        (SELECT DefaultCostCenterId FROM inv.Warehouse WHERE Id = @WarehouseId) AS CostCenterId;
END;
