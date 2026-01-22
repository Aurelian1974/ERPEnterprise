PRINT '=== Începere migrare 050_StoredProcedures_Invoice ===';

-- =============================================
-- Invoice Stored Procedures
-- =============================================

-- sp_Invoice_GetById
IF OBJECT_ID('dbo.sp_Invoice_GetById', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Invoice_GetById;
GO

CREATE PROCEDURE dbo.sp_Invoice_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        i.Id,
        i.DocumentId,
        i.PartnerId,
        p.Nume as PartnerName,
        i.TotalAmount,
        i.VATAmount,
        i.TotalPayment,
        i.Observations,
        i.PartnerAddressSediuId,
        i.PartnerAddressCorespondentaId,
        i.PartnerAddressLivrareId,
        i.PartnerAddressFacturareId,
        i.UserId,
        i.IsActive,
        i.CreatedAt,
        i.CreatedBy,
        i.UpdatedAt,
        i.UpdatedBy,
        i.OwnerCompanyId,
        i.OwnerWorkPlaceId,
        i.OwnerLocationId
    FROM dbo.Invoice i
    INNER JOIN dbo.Partners p ON i.PartnerId = p.Id
    WHERE i.Id = @Id AND i.IsActive = 1;
END
GO

-- sp_Invoice_GetAll
IF OBJECT_ID('dbo.sp_Invoice_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Invoice_GetAll;
GO

CREATE PROCEDURE dbo.sp_Invoice_GetAll
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        i.Id,
        i.DocumentId,
        d.DocumentNumber,
        d.DocumentDate,
        d.DueDate,
        i.PartnerId,
        p.Denumire as PartnerName,
        i.PartnerAddressSediuId,
        i.PartnerAddressCorespondentaId,
        i.PartnerAddressLivrareId,
        i.PartnerAddressFacturareId,
        i.TotalAmount,
        i.VATAmount,
        i.TotalPayment,
        ds.DenumireStare as DocumentStateName,
        i.Observations,
        i.UserId,
        i.IsActive,
        i.CreatedAt,
        i.CreatedBy,
        i.UpdatedAt,
        i.UpdatedBy
    FROM dbo.Invoice i
    INNER JOIN dbo.Document d ON i.DocumentId = d.Id
    INNER JOIN dbo.Partners p ON i.PartnerId = p.Id
    INNER JOIN dbo.DocumentState ds ON d.DocumentStateId = ds.Id
    WHERE i.IsActive = 1
    AND d.IsActive = 1
    AND (@OwnerCompanyId IS NULL OR i.OwnerCompanyId = @OwnerCompanyId)
    ORDER BY d.DocumentDate DESC, d.DocumentNumber DESC;
END
GO

PRINT '✅ Stored procedures pentru Invoice create';

PRINT '=== Migrare 050_StoredProcedures_Invoice completă ===';