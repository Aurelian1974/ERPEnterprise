CREATE OR ALTER PROCEDURE inv.usp_LandedCost_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Documentul CA (02.05, S3 11; macheta s3-01): antetul si liniile, cu intrarea tinta (document, gestiune, varianta, cantitate, cost).
    SET NOCOUNT ON;

    SELECT c.Id, c.Number, c.DocumentDate, c.AccountingDate, c.SourceKind, c.SupplierInvoiceId, c.PartnerId, p.Name AS PartnerName, c.Amount,
        c.AllocationBasis, c.Status, c.Notes, c.StockAmount, c.ExpenseAmount, c.RowVersion
    FROM inv.LandedCost AS c
    LEFT JOIN core.Partner AS p ON p.Id = c.PartnerId
    WHERE c.Id = @Id AND c.CompanyId = @CompanyId;

    SELECT l.LineNumber, l.MovementId, m.WarehouseId, w.Code AS WarehouseCode, m.ItemVariantId, v.Code AS VariantCode, v.Name AS VariantName,
        m.MovementDate, m.SourceDocumentType, m.SourceDocumentNumber, m.QtyBase, m.CostAmount, l.ManualAmount, l.AllocatedAmount
    FROM inv.LandedCostLine AS l
    JOIN inv.LandedCost AS c ON c.Id = l.LandedCostId
    JOIN inv.StockMovement AS m ON m.Id = l.MovementId
    JOIN inv.Warehouse AS w ON w.Id = m.WarehouseId
    JOIN core.ItemVariant AS v ON v.Id = m.ItemVariantId
    WHERE l.LandedCostId = @Id AND c.CompanyId = @CompanyId
    ORDER BY l.LineNumber;
END;
