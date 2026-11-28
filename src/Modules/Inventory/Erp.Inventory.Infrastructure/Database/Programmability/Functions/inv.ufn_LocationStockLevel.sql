CREATE OR ALTER FUNCTION inv.ufn_LocationStockLevel (@CompanyId int)
RETURNS TABLE
AS
RETURN
    -- Nivelul de evidenta efectiv al fiecarei locatii a firmei (02.05, S1 3.8): locatia, apoi punctul ei de lucru, apoi firma.
    -- Source: 1 firma, 2 punct de lucru, 3 locatie.
    SELECT l.Id AS LocationId,
        CAST(COALESCE(lo.StockKeepingLevel, bo.StockKeepingLevel, s.StockKeepingLevel, 1) AS tinyint) AS StockKeepingLevel,
        CAST(CASE WHEN lo.Id IS NOT NULL THEN 3 WHEN bo.Id IS NOT NULL THEN 2 ELSE 1 END AS tinyint) AS Source
    FROM core.Location AS l
    LEFT JOIN inv.StockLevelOverride AS lo ON lo.LocationId = l.Id
    LEFT JOIN inv.StockLevelOverride AS bo ON bo.BranchId = l.BranchId
    LEFT JOIN inv.StockSettings AS s ON s.CompanyId = l.CompanyId
    WHERE l.CompanyId = @CompanyId;
