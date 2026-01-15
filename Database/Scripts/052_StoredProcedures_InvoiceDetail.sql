PRINT '=== Începere migrare 052_StoredProcedures_InvoiceDetail ===';

-- =============================================
-- InvoiceDetail Stored Procedures
-- =============================================

-- sp_InvoiceDetail_GetByInvoiceId
IF OBJECT_ID('dbo.sp_InvoiceDetail_GetByInvoiceId', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_InvoiceDetail_GetByInvoiceId;
GO

CREATE PROCEDURE dbo.sp_InvoiceDetail_GetByInvoiceId
    @InvoiceId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        id.Id,
        id.InvoiceId,
        id.DocumentDetailId,
        dd.ItemId,
        a.ArticolName as ItemName,
        a.ArticolCode,
        dd.Quantity,
        dd.UnitMeasure,
        id.UnitPrice,
        id.VATRate,
        id.VATAmount,
        id.LineTotal,
        id.IsActive,
        id.CreatedAt,
        id.CreatedBy,
        id.UpdatedAt,
        id.UpdatedBy,
        id.OwnerCompanyId,
        id.OwnerWorkPlaceId,
        id.OwnerLocationId
    FROM dbo.InvoiceDetail id
    INNER JOIN dbo.DocumentDetail dd ON id.DocumentDetailId = dd.Id
    INNER JOIN dbo.Articole a ON dd.ItemId = a.Id
    WHERE id.InvoiceId = @InvoiceId AND id.IsActive = 1
    ORDER BY id.CreatedAt;
END
GO

PRINT '✅ Stored procedures pentru InvoiceDetail create';

PRINT '=== Migrare 052_StoredProcedures_InvoiceDetail completă ===';