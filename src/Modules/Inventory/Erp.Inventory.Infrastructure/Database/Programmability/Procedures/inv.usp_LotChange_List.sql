CREATE OR ALTER PROCEDURE inv.usp_LotChange_List
    @CompanyId int,
    @Top       int = 200
AS
BEGIN
    -- Documentele SL ale firmei, cele mai noi intai.
    SET NOCOUNT ON;
    SELECT TOP (@Top) d.Id, d.Number, d.WarehouseId, w.Code AS WarehouseCode, d.DocumentDate, d.Status,
        (SELECT COUNT(*) FROM inv.LotChangeLine AS l WHERE l.LotChangeId = d.Id) AS LineCount
    FROM inv.LotChange AS d JOIN inv.Warehouse AS w ON w.Id = d.WarehouseId
    WHERE d.CompanyId = @CompanyId
    ORDER BY d.DocumentDate DESC, d.Id DESC;
END;
