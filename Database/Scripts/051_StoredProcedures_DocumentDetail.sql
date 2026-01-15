PRINT '=== Începere migrare 051_StoredProcedures_DocumentDetail ===';

-- =============================================
-- DocumentDetail Stored Procedures
-- =============================================

-- sp_DocumentDetail_GetByDocumentId
IF OBJECT_ID('dbo.sp_DocumentDetail_GetByDocumentId', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_DocumentDetail_GetByDocumentId;
GO

CREATE PROCEDURE dbo.sp_DocumentDetail_GetByDocumentId
    @DocumentId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        dd.Id,
        dd.DocumentId,
        dd.ItemId,
        a.ArticolName as ItemName,
        a.ArticolCode,
        dd.Quantity,
        dd.UnitMeasure,
        dd.IsActive,
        dd.CreatedAt,
        dd.CreatedBy,
        dd.UpdatedAt,
        dd.UpdatedBy,
        dd.OwnerCompanyId,
        dd.OwnerWorkPlaceId,
        dd.OwnerLocationId
    FROM dbo.DocumentDetail dd
    INNER JOIN dbo.Articole a ON dd.ItemId = a.Id
    WHERE dd.DocumentId = @DocumentId AND dd.IsActive = 1
    ORDER BY dd.CreatedAt;
END
GO

PRINT '✅ Stored procedures pentru DocumentDetail create';

PRINT '=== Migrare 051_StoredProcedures_DocumentDetail completă ===';