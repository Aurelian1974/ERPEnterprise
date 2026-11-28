CREATE OR ALTER PROCEDURE inv.usp_CostRecalcQueue_List
    @CompanyId int = NULL,
    @Top       int = 500
AS
BEGIN
    -- Coada de recalculare (02.05, S3 3.5; jobul la 15 minute si ecranul s3-02), cele mai vechi date intai.
    SET NOCOUNT ON;

    SELECT TOP (@Top) q.CompanyId, q.WarehouseId, w.Code AS WarehouseCode, q.DepotId, q.ItemVariantId, v.Code AS VariantCode, q.LotId, q.FromDate, q.EnqueuedAt
    FROM inv.CostRecalcQueue AS q
    JOIN inv.Warehouse AS w ON w.Id = q.WarehouseId
    JOIN core.ItemVariant AS v ON v.Id = q.ItemVariantId
    WHERE @CompanyId IS NULL OR q.CompanyId = @CompanyId
    ORDER BY q.FromDate, q.Id;
END;
