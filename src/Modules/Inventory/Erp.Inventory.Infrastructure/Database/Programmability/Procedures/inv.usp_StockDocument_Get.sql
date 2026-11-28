CREATE OR ALTER PROCEDURE inv.usp_StockDocument_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Documentul de stoc cu liniile lui (02.05, S2 11). Documentul altei firme nu intoarce nimic.
    SET NOCOUNT ON;

    SELECT d.Id, d.DocumentType, d.Number, d.DocumentDate, d.AccountingDate, d.FromDepotId, d.FromWarehouseId, fw.Code AS FromWarehouseCode,
        fw.DimensionValueId AS FromWarehouseDimensionId, d.ToDepotId, d.ToWarehouseId, tw.Code AS ToWarehouseCode, tw.DimensionValueId AS ToWarehouseDimensionId,
        d.ReasonId, r.Code AS ReasonCode, r.Name AS ReasonName, r.RequiresAttachment, d.PartnerId, d.CostCenterId, d.ProjectId, d.EmployeeId,
        d.TransferMode, d.TransitShipmentId, d.RelatedDocumentId, d.IsReversal, d.Status, d.ApprovalStatus, d.ApprovalNote, d.Notes, d.IsForcedNegative,
        d.TotalCost, d.ValidatedAt, d.ReversedByDocumentId, d.CreatedBy, d.CreatedAt, d.ModifiedAt, d.RowVersion
    FROM inv.StockDocument AS d
    LEFT JOIN inv.Warehouse AS fw ON fw.Id = d.FromWarehouseId
    LEFT JOIN inv.Warehouse AS tw ON tw.Id = d.ToWarehouseId
    LEFT JOIN inv.MovementReason AS r ON r.Id = d.ReasonId
    WHERE d.Id = @Id AND d.CompanyId = @CompanyId;

    SELECT l.Id, l.LineNumber, l.ItemVariantId, v.Code AS VariantCode, l.ItemNameSnapshot, l.UomId, l.Qty, l.UomFactor, l.QtyBase, l.UnitCost, l.FromDepotId,
        l.FromWarehouseId, l.ToDepotId, l.ToWarehouseId, l.CostCenterId, l.ProjectId, l.ReceivedQtyBase, l.DifferenceQtyBase, l.DifferenceResolution,
        l.ReversedLineId, l.UnitCostPosted, l.CostAmountPosted
    FROM inv.StockDocumentLine AS l
    JOIN inv.StockDocument AS d ON d.Id = l.DocumentId
    JOIN core.ItemVariant AS v ON v.Id = l.ItemVariantId
    WHERE l.DocumentId = @Id AND d.CompanyId = @CompanyId
    ORDER BY l.LineNumber;
END;
