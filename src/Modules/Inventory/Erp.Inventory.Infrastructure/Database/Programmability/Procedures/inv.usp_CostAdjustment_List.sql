CREATE OR ALTER PROCEDURE inv.usp_CostAdjustment_List
    @CompanyId int,
    @Kind      tinyint = NULL,
    @Top       int = 200
AS
BEGIN
    -- Ultimele ajustari de cost (macheta s3-02): recalculari (1), regularizari ale negativului (2), costuri adaugate (3), diferente de pret (4).
    SET NOCOUNT ON;

    SELECT TOP (@Top) a.Id, a.Kind, a.AccountingDate, w.Code AS WarehouseCode, v.Code AS VariantCode, a.MovementId, a.SourceDocumentType, a.SourceDocumentId,
        a.Amount, a.InStock, a.CreatedAt
    FROM inv.CostAdjustment AS a
    JOIN inv.Warehouse AS w ON w.Id = a.WarehouseId
    JOIN core.ItemVariant AS v ON v.Id = a.ItemVariantId
    WHERE a.CompanyId = @CompanyId AND (@Kind IS NULL OR a.Kind = @Kind)
    ORDER BY a.Id DESC;
END;
