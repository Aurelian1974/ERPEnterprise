CREATE OR ALTER PROCEDURE inv.usp_RetailDischarge_List
    @CompanyId   int,
    @WarehouseId int = NULL,
    @Top         int = 200
AS
BEGIN
    -- Descarcarile lunare validate (02.05, S3 11; macheta s3-03), cele mai noi intai.
    SET NOCOUNT ON;

    SELECT TOP (@Top) d.Id, d.Number, d.WarehouseId, w.Code AS WarehouseCode, w.DimensionValueId AS WarehouseDimensionId, d.PeriodStart, d.OpeningRetail,
        d.RetailIn, d.RetailOut, d.OpeningMarkup, d.MarkupIn, d.OpeningVat, d.VatIn, d.SalesVat, d.SalesNet, d.K, d.Markup, d.Cost, d.ValidatedAt
    FROM inv.RetailDischarge AS d
    JOIN inv.Warehouse AS w ON w.Id = d.WarehouseId
    WHERE d.CompanyId = @CompanyId AND (@WarehouseId IS NULL OR d.WarehouseId = @WarehouseId)
    ORDER BY d.PeriodStart DESC, d.Id DESC;
END;
