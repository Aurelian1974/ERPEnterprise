CREATE OR ALTER PROCEDURE inv.usp_Depot_List
    @CompanyId int
AS
BEGIN
    -- Ecranul Depozite (02.05, S1 11; macheta s1-05): depozitele firmei, gestiunile din ele si regulile de propunere.
    SET NOCOUNT ON;

    SELECT d.Id, d.Code, d.Name, d.LocationId, l.Name AS LocationName, d.DefaultWarehouseId, d.DimensionValueId, d.IsBlocked, d.BlockReason,
        d.IsActive, d.RowVersion
    FROM inv.Depot AS d
    JOIN core.Location AS l ON l.Id = d.LocationId
    WHERE d.CompanyId = @CompanyId
    ORDER BY l.CodeKey, d.CodeKey;

    SELECT dw.DepotId, dw.WarehouseId, w.Code AS WarehouseCode, w.Name AS WarehouseName
    FROM inv.DepotWarehouse AS dw
    JOIN inv.Depot AS d ON d.Id = dw.DepotId
    JOIN inv.Warehouse AS w ON w.Id = dw.WarehouseId
    WHERE d.CompanyId = @CompanyId AND dw.IsActive = 1
    ORDER BY w.CodeKey;

    SELECT r.Id, r.DepotId, r.ItemGroupId, g.Name AS ItemGroupName, r.ItemId, i.Name AS ItemName, r.WarehouseId, w.Code AS WarehouseCode
    FROM inv.DepotWarehouseRule AS r
    JOIN inv.Depot AS d ON d.Id = r.DepotId
    JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId
    LEFT JOIN core.ItemGroup AS g ON g.Id = r.ItemGroupId
    LEFT JOIN core.Item AS i ON i.Id = r.ItemId
    WHERE d.CompanyId = @CompanyId
    ORDER BY CASE WHEN r.ItemId IS NULL THEN 1 ELSE 0 END, i.Name, g.Name;
END;
