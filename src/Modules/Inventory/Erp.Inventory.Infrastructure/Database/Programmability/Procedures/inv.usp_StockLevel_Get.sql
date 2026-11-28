CREATE OR ALTER PROCEDURE inv.usp_StockLevel_Get
    @CompanyId int
AS
BEGIN
    -- Nivelul de evidenta (02.05, S1 3.8; macheta s1-05): al firmei, suprascrierile pe punct de lucru si locatie, apoi locatiile active cu
    -- nivelul efectiv si de unde vine (1 firma, 2 punct de lucru, 3 locatie).
    SET NOCOUNT ON;

    SELECT ISNULL((SELECT StockKeepingLevel FROM inv.StockSettings WHERE CompanyId = @CompanyId), 1) AS CompanyLevel;

    SELECT o.Id, o.BranchId, b.Name AS BranchName, o.LocationId, l.Name AS LocationName, o.StockKeepingLevel
    FROM inv.StockLevelOverride AS o
    LEFT JOIN core.Branch AS b ON b.Id = o.BranchId
    LEFT JOIN core.Location AS l ON l.Id = o.LocationId
    WHERE o.CompanyId = @CompanyId
    ORDER BY CASE WHEN o.BranchId IS NULL THEN 1 ELSE 0 END, b.Name, l.Name;

    SELECT l.Id, l.Code, l.Name, l.BranchId, e.StockKeepingLevel, e.Source
    FROM core.Location AS l
    JOIN inv.ufn_LocationStockLevel(@CompanyId) AS e ON e.LocationId = l.Id
    WHERE l.CompanyId = @CompanyId AND l.IsActive = 1
    ORDER BY l.CodeKey;
END;
