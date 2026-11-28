CREATE OR ALTER PROCEDURE inv.usp_TransitShipment_List
    @CompanyId     int,
    @ToWarehouseId int = NULL,
    @OpenOnly      bit = 1
AS
BEGIN
    -- Expeditiile in tranzit (02.05, S2 3.4; „De primit” si raportul Transferuri in tranzit), cu vechimea lor.
    SET NOCOUNT ON;

    SELECT s.Id, s.NpDocumentId, d.Number AS NpNumber, s.FromWarehouseId, fw.Code AS FromWarehouseCode, fw.DimensionValueId AS FromWarehouseDimensionId,
        s.ToWarehouseId, tw.Code AS ToWarehouseCode, tw.DimensionValueId AS ToWarehouseDimensionId, s.ShippedDate, s.Status,
        DATEDIFF(DAY, s.ShippedDate, CAST(SYSDATETIME() AS date)) AS AgeDays
    FROM inv.TransitShipment AS s
    JOIN inv.StockDocument AS d ON d.Id = s.NpDocumentId
    JOIN inv.Warehouse AS fw ON fw.Id = s.FromWarehouseId
    JOIN inv.Warehouse AS tw ON tw.Id = s.ToWarehouseId
    WHERE s.CompanyId = @CompanyId AND (@ToWarehouseId IS NULL OR s.ToWarehouseId = @ToWarehouseId) AND (@OpenOnly = 0 OR s.Status IN (1, 2))
    ORDER BY s.ShippedDate, s.Id;
END;
