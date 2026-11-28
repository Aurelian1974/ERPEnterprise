CREATE OR ALTER PROCEDURE inv.usp_CostLayer_List
    @CompanyId     int,
    @ItemVariantId int,
    @WarehouseId   int = NULL,
    @OpenOnly      bit = 1
AS
BEGIN
    -- Straturile de cost ale unei variante (macheta s3-05), in ordinea consumului FIFO, cu documentul intrarii si, la transfer, al stratului din
    -- care provin; apoi ultimele 50 de consumuri ale acestor straturi.
    SET NOCOUNT ON;

    DECLARE @Layers TABLE (Id bigint PRIMARY KEY);
    INSERT INTO @Layers
    SELECT c.Id FROM inv.CostLayer AS c
    WHERE c.CompanyId = @CompanyId AND c.ItemVariantId = @ItemVariantId AND (@WarehouseId IS NULL OR c.WarehouseId = @WarehouseId)
      AND (@OpenOnly = 0 OR c.QtyRemaining <> 0);

    SELECT c.Id, c.WarehouseId, w.Code AS WarehouseCode, d.Code AS DepotCode, c.LotId,
        CONCAT(m.SourceDocumentType, N' ', ISNULL(m.SourceDocumentNumber, CAST(m.SourceDocumentId AS nvarchar(20)))) AS SourceDocument,
        CASE WHEN o.Id IS NULL THEN NULL ELSE CONCAT(om.SourceDocumentType, N' ', ISNULL(om.SourceDocumentNumber, CAST(om.SourceDocumentId AS nvarchar(20)))) END
            AS OriginDocument,
        c.LayerDate, c.QtyIn, c.QtyRemaining, c.UnitCost, c.ValueRemaining, c.IsNegative
    FROM inv.CostLayer AS c
    JOIN @Layers AS x ON x.Id = c.Id
    JOIN inv.Warehouse AS w ON w.Id = c.WarehouseId
    LEFT JOIN inv.Depot AS d ON d.Id = c.DepotId
    JOIN inv.StockMovement AS m ON m.Id = c.MovementId
    LEFT JOIN inv.CostLayer AS o ON o.Id = c.OriginLayerId
    LEFT JOIN inv.StockMovement AS om ON om.Id = o.MovementId
    ORDER BY w.Code, c.LayerDate, c.PostingSeq;

    SELECT TOP (50) m.MovementDate,
        CONCAT(m.SourceDocumentType, N' ', ISNULL(m.SourceDocumentNumber, CAST(m.SourceDocumentId AS nvarchar(20)))) AS Outbound, -x.Qty AS Qty,
        CONCAT(lm.SourceDocumentType, N' ', ISNULL(lm.SourceDocumentNumber, CAST(lm.SourceDocumentId AS nvarchar(20)))) AS LayerDocument, c.UnitCost,
        x.CostAmount AS Value
    FROM inv.CostLayerConsumption AS x
    JOIN inv.CostLayer AS c ON c.Id = x.CostLayerId
    JOIN inv.StockMovement AS m ON m.Id = x.OutboundMovementId
    JOIN inv.StockMovement AS lm ON lm.Id = c.MovementId
    WHERE c.CompanyId = @CompanyId AND c.ItemVariantId = @ItemVariantId AND (@WarehouseId IS NULL OR c.WarehouseId = @WarehouseId)
    ORDER BY m.MovementDate DESC, m.PostingSeq DESC, x.Id DESC;
END;
