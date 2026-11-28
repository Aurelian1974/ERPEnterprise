CREATE OR ALTER PROCEDURE inv.usp_Depot_ProposeWarehouse
    @DepotId       int,
    @ItemVariantId int
AS
BEGIN
    -- Gestiunea propusa pe linia unui document la nivelul „depozit” (02.05, S1 3.8, decizia 8): regula pe articol, apoi pe grupa articolului
    -- (cea mai apropiata in arbore), apoi gestiunea implicita a depozitului. Doar gestiunile active din depozit; NULL daca nu gaseste.
    SET NOCOUNT ON;

    DECLARE @ItemId int, @GroupId int;
    SELECT @ItemId = i.Id, @GroupId = i.ItemGroupId FROM core.ItemVariant AS v JOIN core.Item AS i ON i.Id = v.ItemId WHERE v.Id = @ItemVariantId;

    WITH Groups AS (
        SELECT g.Id, g.ParentId, 1 AS Depth FROM core.ItemGroup AS g WHERE g.Id = @GroupId
        UNION ALL
        SELECT p.Id, p.ParentId, c.Depth + 1 FROM core.ItemGroup AS p JOIN Groups AS c ON c.ParentId = p.Id WHERE c.Depth < 20
    ),
    Candidates AS (
        SELECT r.WarehouseId, 0 AS Priority FROM inv.DepotWarehouseRule AS r WHERE r.DepotId = @DepotId AND r.ItemId = @ItemId
        UNION ALL
        SELECT r.WarehouseId, g.Depth FROM inv.DepotWarehouseRule AS r JOIN Groups AS g ON g.Id = r.ItemGroupId WHERE r.DepotId = @DepotId
        UNION ALL
        SELECT d.DefaultWarehouseId, 1000 FROM inv.Depot AS d WHERE d.Id = @DepotId AND d.DefaultWarehouseId IS NOT NULL
    )
    SELECT TOP (1) c.WarehouseId
    FROM Candidates AS c
    JOIN inv.DepotWarehouse AS dw ON dw.DepotId = @DepotId AND dw.WarehouseId = c.WarehouseId AND dw.IsActive = 1
    JOIN inv.Warehouse AS w ON w.Id = c.WarehouseId AND w.IsActive = 1
    ORDER BY c.Priority;
END;
