PRINT '=== Începere migrare 054_StoredProcedures_Invoice_Update ===';

-- =============================================
-- Invoice Update Stored Procedures
-- =============================================

-- sp_Document_Update
IF OBJECT_ID('dbo.sp_Document_Update', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Document_Update;
GO

CREATE PROCEDURE dbo.sp_Document_Update
    @Id UNIQUEIDENTIFIER,
    @DocumentDate DATETIME2,
    @DueDate DATETIME2 = NULL,
    @DocumentNumber NVARCHAR(50),
    @DocumentStateId UNIQUEIDENTIFIER,
    @Observations NVARCHAR(500) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Document
    SET
        DocumentDate = @DocumentDate,
        DueDate = @DueDate,
        DocumentNumber = @DocumentNumber,
        DocumentStateId = @DocumentStateId,
        Observations = @Observations,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;

    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- sp_Invoice_Update
IF OBJECT_ID('dbo.sp_Invoice_Update', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Invoice_Update;
GO

CREATE PROCEDURE dbo.sp_Invoice_Update
    @Id UNIQUEIDENTIFIER,
    @PartnerId UNIQUEIDENTIFIER,
    @Observations NVARCHAR(500) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Invoice
    SET
        PartnerId = @PartnerId,
        Observations = @Observations,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;

    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- sp_InvoiceDetail_UpdateLineItems
IF OBJECT_ID('dbo.sp_InvoiceDetail_UpdateLineItems', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_InvoiceDetail_UpdateLineItems;
GO

CREATE PROCEDURE dbo.sp_InvoiceDetail_UpdateLineItems
    @InvoiceId UNIQUEIDENTIFIER,
    @LineItems dbo.InvoiceLineItemType READONLY,
    @UpdatedBy UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    -- Delete existing line items
    DELETE FROM dbo.InvoiceDetail WHERE InvoiceId = @InvoiceId;

    -- Insert new line items
    INSERT INTO dbo.InvoiceDetail (
        Id,
        InvoiceId,
        ItemId,
        Quantity,
        UnitMeasure,
        UnitPrice,
        VATRate,
        VATAmount,
        LineTotal,
        IsActive,
        CreatedAt,
        CreatedBy,
        UpdatedAt,
        UpdatedBy
    )
    SELECT
        NEWID(),
        @InvoiceId,
        ItemId,
        Quantity,
        UnitMeasure,
        UnitPrice,
        VATRate,
        (Quantity * UnitPrice * VATRate / 100), -- VAT Amount
        (Quantity * UnitPrice), -- Line Total before VAT
        1,
        GETDATE(),
        @UpdatedBy,
        GETDATE(),
        @UpdatedBy
    FROM @LineItems;

    -- Update invoice totals
    UPDATE dbo.Invoice
    SET
        TotalAmount = (
            SELECT SUM(LineTotal + VATAmount)
            FROM dbo.InvoiceDetail
            WHERE InvoiceId = @InvoiceId AND IsActive = 1
        ),
        VATAmount = (
            SELECT SUM(VATAmount)
            FROM dbo.InvoiceDetail
            WHERE InvoiceId = @InvoiceId AND IsActive = 1
        ),
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @InvoiceId;

    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- First, create the table type for line items if it doesn't exist
IF TYPE_ID('dbo.InvoiceLineItemType') IS NULL
BEGIN
    CREATE TYPE dbo.InvoiceLineItemType AS TABLE (
        ItemId UNIQUEIDENTIFIER,
        Quantity DECIMAL(18,2),
        UnitMeasure NVARCHAR(20),
        UnitPrice DECIMAL(18,2),
        VATRate DECIMAL(5,2)
    );
END
GO

-- sp_Invoice_UpdateComplete - Master procedure for updating entire invoice
IF OBJECT_ID('dbo.sp_Invoice_UpdateComplete', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Invoice_UpdateComplete;
GO

CREATE PROCEDURE dbo.sp_Invoice_UpdateComplete
    @InvoiceId UNIQUEIDENTIFIER,
    @DocumentDate DATETIME2,
    @DueDate DATETIME2 = NULL,
    @DocumentNumber NVARCHAR(50),
    @DocumentStateId UNIQUEIDENTIFIER,
    @DocumentObservations NVARCHAR(500) = NULL,
    @PartnerId UNIQUEIDENTIFIER,
    @InvoiceObservations NVARCHAR(500) = NULL,
    @LineItems dbo.InvoiceLineItemType READONLY,
    @UpdatedBy UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    BEGIN TRY
        -- Get DocumentId from Invoice
        DECLARE @DocumentId UNIQUEIDENTIFIER;
        SELECT @DocumentId = DocumentId FROM dbo.Invoice WHERE Id = @InvoiceId;

        IF @DocumentId IS NULL
        BEGIN
            RAISERROR('Invoice not found', 16, 1);
            RETURN;
        END

        -- Update Document
        EXEC dbo.sp_Document_Update
            @Id = @DocumentId,
            @DocumentDate = @DocumentDate,
            @DueDate = @DueDate,
            @DocumentNumber = @DocumentNumber,
            @DocumentStateId = @DocumentStateId,
            @Observations = @DocumentObservations,
            @UpdatedBy = @UpdatedBy;

        -- Update Invoice
        EXEC dbo.sp_Invoice_Update
            @Id = @InvoiceId,
            @PartnerId = @PartnerId,
            @Observations = @InvoiceObservations,
            @UpdatedBy = @UpdatedBy;

        -- Update Line Items
        EXEC dbo.sp_InvoiceDetail_UpdateLineItems
            @InvoiceId = @InvoiceId,
            @LineItems = @LineItems,
            @UpdatedBy = @UpdatedBy;

        COMMIT TRANSACTION;

        SELECT 1 AS Success;

    END TRY
    BEGIN CATCH
        ROLLBACK TRANSACTION;

        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        RAISERROR(@ErrorMessage, 16, 1);
    END CATCH
END
GO

PRINT '✅ Stored procedures pentru Invoice Update create';

PRINT '=== Migrare 054_StoredProcedures_Invoice_Update completă ===';