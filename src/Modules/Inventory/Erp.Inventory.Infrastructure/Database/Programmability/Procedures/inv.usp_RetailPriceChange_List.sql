CREATE OR ALTER PROCEDURE inv.usp_RetailPriceChange_List
    @CompanyId   int,
    @WarehouseId int = NULL,
    @Status      tinyint = NULL,
    @Top         int = 200
AS
BEGIN
    -- Lista documentelor MP (02.05, S3 11; macheta s3-04), cele mai noi intai.
    SET NOCOUNT ON;

    SELECT TOP (@Top) d.Id, d.Number, d.WarehouseId, w.Code AS WarehouseCode, w.DimensionValueId AS WarehouseDimensionId, d.DocumentDate, d.Status,
        d.DiffAmount, d.DiffVatAmount, (SELECT COUNT(*) FROM inv.RetailPriceChangeLine AS l WHERE l.RetailPriceChangeId = d.Id) AS LineCount
    FROM inv.RetailPriceChange AS d
    JOIN inv.Warehouse AS w ON w.Id = d.WarehouseId
    WHERE d.CompanyId = @CompanyId AND (@WarehouseId IS NULL OR d.WarehouseId = @WarehouseId) AND (@Status IS NULL OR d.Status = @Status)
    ORDER BY d.DocumentDate DESC, d.Id DESC;
END;
