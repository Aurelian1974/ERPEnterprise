CREATE OR ALTER PROCEDURE inv.usp_StockLevel_Set
    @CompanyId  int,
    @BranchId   int = NULL,
    @LocationId int = NULL,
    @Level      tinyint = NULL,
    @UserId     int
AS
BEGIN
    -- Nivelul de evidenta pe firma (fara punct de lucru si locatie), pe punct de lucru sau pe locatie (02.05, S1 3.8); @Level NULL sterge
    -- suprascrierea. Nivelul se schimba doar fara stoc si fara ciorne in perimetru (54016; ciornele se adauga cu documentele S2) si fara
    -- legaturi care l-ar contrazice: gestiuni legate direct de o locatie care trece pe „depozit”, depozite active intr-o locatie care trece pe
    -- „gestiune” (54017). Referinte: 54018.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @BranchId IS NOT NULL AND @LocationId IS NOT NULL
        THROW 54018, N'Nivelul se setează pe punctul de lucru sau pe locație, nu pe amândouă.', 1;
    IF @BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Branch WHERE Id = @BranchId AND CompanyId = @CompanyId)
        THROW 54018, N'Punctul de lucru nu aparține firmei.', 1;
    IF @LocationId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Location WHERE Id = @LocationId AND CompanyId = @CompanyId)
        THROW 54018, N'Locația nu aparține firmei.', 1;
    IF @BranchId IS NULL AND @LocationId IS NULL AND @Level IS NULL
        THROW 54018, N'Firma are mereu un nivel de evidență.', 1;

    DECLARE @Before TABLE (LocationId int PRIMARY KEY, StockKeepingLevel tinyint);
    INSERT INTO @Before SELECT LocationId, StockKeepingLevel FROM inv.ufn_LocationStockLevel(@CompanyId);

    IF @BranchId IS NULL AND @LocationId IS NULL
        UPDATE inv.StockSettings SET StockKeepingLevel = @Level, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE CompanyId = @CompanyId;
    ELSE
    BEGIN
        DELETE FROM inv.StockLevelOverride WHERE CompanyId = @CompanyId AND (BranchId = @BranchId OR LocationId = @LocationId);
        IF @Level IS NOT NULL
            INSERT INTO inv.StockLevelOverride (CompanyId, BranchId, LocationId, StockKeepingLevel, CreatedBy)
            VALUES (@CompanyId, @BranchId, @LocationId, @Level, @UserId);
    END;

    DECLARE @Changed TABLE (LocationId int PRIMARY KEY, StockKeepingLevel tinyint);
    INSERT INTO @Changed
    SELECT a.LocationId, a.StockKeepingLevel
    FROM inv.ufn_LocationStockLevel(@CompanyId) AS a
    JOIN @Before AS b ON b.LocationId = a.LocationId
    WHERE a.StockKeepingLevel <> b.StockKeepingLevel;

    IF EXISTS (SELECT 1 FROM @Changed AS c
               JOIN inv.Warehouse AS w ON w.LocationId = c.LocationId
               JOIN inv.Stock AS s ON s.WarehouseId = w.Id AND s.QtyOnHand <> 0)
       OR EXISTS (SELECT 1 FROM @Changed AS c
                  JOIN inv.Depot AS d ON d.LocationId = c.LocationId
                  JOIN inv.Stock AS s ON s.DepotId = d.Id AND s.QtyOnHand <> 0)
        THROW 54016, N'Gestiunile sau depozitele locației au stoc; goliți-le prin note de mișcare înainte de schimbarea nivelului.', 1;
    IF EXISTS (SELECT 1 FROM @Changed AS c JOIN inv.Warehouse AS w ON w.LocationId = c.LocationId WHERE c.StockKeepingLevel = 2 AND w.IsActive = 1)
        THROW 54017, N'Locația are gestiuni legate direct; mutați-le întâi în depozite (golind stocul prin note de mișcare).', 1;
    IF EXISTS (SELECT 1 FROM @Changed AS c JOIN inv.Depot AS d ON d.LocationId = c.LocationId WHERE c.StockKeepingLevel = 1 AND d.IsActive = 1)
        THROW 54017, N'Locația are depozite active; dezactivați-le întâi (golind stocul prin note de mișcare).', 1;
END;
