CREATE OR ALTER PROCEDURE inv.usp_StockDocument_List
    @CompanyId    int,
    @DocumentType varchar(2) = NULL,
    @Status       tinyint = NULL,
    @WarehouseId  int = NULL,
    @DateFrom     date = NULL,
    @DateTo       date = NULL,
    @Top          int = 200
AS
BEGIN
    -- Listele documentelor de stoc (02.05, S2 11; machetele s2-01, s2-03, s2-04), cele mai noi intai. Restrangerea pe gestiunile permise o face
    -- apelantul, dupa valorile de dimensiune ale gestiunilor.
    SET NOCOUNT ON;

    SELECT TOP (@Top) d.Id, d.DocumentType, d.Number, d.DocumentDate, d.Status, d.ApprovalStatus, d.IsReversal, d.FromWarehouseId, fw.Code AS FromWarehouseCode,
        fw.DimensionValueId AS FromWarehouseDimensionId, d.ToWarehouseId, tw.Code AS ToWarehouseCode, tw.DimensionValueId AS ToWarehouseDimensionId,
        r.Name AS ReasonName, d.TotalCost, (SELECT COUNT(*) FROM inv.StockDocumentLine AS l WHERE l.DocumentId = d.Id) AS LineCount
    FROM inv.StockDocument AS d
    LEFT JOIN inv.Warehouse AS fw ON fw.Id = d.FromWarehouseId
    LEFT JOIN inv.Warehouse AS tw ON tw.Id = d.ToWarehouseId
    LEFT JOIN inv.MovementReason AS r ON r.Id = d.ReasonId
    WHERE d.CompanyId = @CompanyId
      AND (@DocumentType IS NULL OR d.DocumentType = @DocumentType)
      AND (@Status IS NULL OR d.Status = @Status)
      AND (@WarehouseId IS NULL OR d.FromWarehouseId = @WarehouseId OR d.ToWarehouseId = @WarehouseId)
      AND (@DateFrom IS NULL OR d.DocumentDate >= @DateFrom) AND (@DateTo IS NULL OR d.DocumentDate <= @DateTo)
    ORDER BY d.DocumentDate DESC, d.Id DESC;
END;
