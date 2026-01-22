-- 057_Fix_InvoiceDetail_UpdateLineItems.sql
-- Fix for detail table saving issue - updates sp_InvoiceDetail_UpdateLineItems
-- to properly handle DocumentDetail and InvoiceDetail relationships

PRINT '=== Begin migration 057_Fix_InvoiceDetail_UpdateLineItems ===';

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Fix sp_InvoiceDetail_UpdateLineItems to properly insert into both DocumentDetail and InvoiceDetail
IF OBJECT_ID('dbo.sp_InvoiceDetail_UpdateLineItems', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_InvoiceDetail_UpdateLineItems;
GO

CREATE PROCEDURE dbo.sp_InvoiceDetail_UpdateLineItems
    @InvoiceId UNIQUEIDENTIFIER,
    @DocumentId UNIQUEIDENTIFIER,
    @LineItems dbo.InvoiceLineItemType READONLY,
    @UpdatedBy UNIQUEIDENTIFIER,
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Delete existing invoice detail line items
    DELETE FROM dbo.InvoiceDetail WHERE InvoiceId = @InvoiceId;

    -- Delete existing document detail line items
    DELETE FROM dbo.DocumentDetail WHERE DocumentId = @DocumentId;

    -- Variables for totals
    DECLARE @TotalAmount DECIMAL(18,2) = 0;
    DECLARE @TotalVAT DECIMAL(18,2) = 0;

    -- Process each line item
    DECLARE @ItemId UNIQUEIDENTIFIER, @Quantity DECIMAL(18,2), @UnitMeasure NVARCHAR(20),
            @UnitPrice DECIMAL(18,2), @VATRate DECIMAL(5,2);

    DECLARE line_cursor CURSOR FOR
    SELECT ItemId, Quantity, UnitMeasure, UnitPrice, VATRate FROM @LineItems;

    OPEN line_cursor;
    FETCH NEXT FROM line_cursor INTO @ItemId, @Quantity, @UnitMeasure, @UnitPrice, @VATRate;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        -- Insert DocumentDetail first (contains ItemId, Quantity, UnitMeasure)
        DECLARE @DocumentDetailId UNIQUEIDENTIFIER = NEWID();
        INSERT INTO dbo.DocumentDetail (
            Id, DocumentId, ItemId, Quantity, UnitMeasure,
            CreatedBy, UpdatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
        ) VALUES (
            @DocumentDetailId, @DocumentId, @ItemId, @Quantity, @UnitMeasure,
            @UpdatedBy, @UpdatedBy, @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId
        );

        -- Calculate line totals
        DECLARE @LineTotal DECIMAL(18,2) = @Quantity * @UnitPrice;
        DECLARE @VATAmount DECIMAL(18,2) = @LineTotal * (@VATRate / 100);

        -- Insert InvoiceDetail with reference to DocumentDetail (contains pricing info)
        INSERT INTO dbo.InvoiceDetail (
            Id, InvoiceId, DocumentDetailId, UnitPrice, VATRate, VATAmount, LineTotal,
            CreatedBy, UpdatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
        ) VALUES (
            NEWID(), @InvoiceId, @DocumentDetailId, @UnitPrice, @VATRate, @VATAmount, @LineTotal + @VATAmount,
            @UpdatedBy, @UpdatedBy, @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId
        );

        -- Accumulate totals
        SET @TotalAmount = @TotalAmount + @LineTotal;
        SET @TotalVAT = @TotalVAT + @VATAmount;

        FETCH NEXT FROM line_cursor INTO @ItemId, @Quantity, @UnitMeasure, @UnitPrice, @VATRate;
    END

    CLOSE line_cursor;
    DEALLOCATE line_cursor;

    -- Update invoice totals
    UPDATE dbo.Invoice
    SET
        TotalAmount = @TotalAmount,
        VATAmount = @TotalVAT,
        TotalPayment = @TotalAmount + @TotalVAT,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @InvoiceId;

    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT 'Fixed sp_InvoiceDetail_UpdateLineItems to properly handle DocumentDetail and InvoiceDetail';

-- Update sp_Invoice_UpdateComplete to pass DocumentId and owner fields
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
    @PartnerAddressSediuId UNIQUEIDENTIFIER = NULL,
    @PartnerAddressCorespondentaId UNIQUEIDENTIFIER = NULL,
    @PartnerAddressLivrareId UNIQUEIDENTIFIER = NULL,
    @PartnerAddressFacturareId UNIQUEIDENTIFIER = NULL,
    @PartnerContactId UNIQUEIDENTIFIER = NULL,
    @PartnerBankAccountId UNIQUEIDENTIFIER = NULL,
    @InvoiceObservations NVARCHAR(500) = NULL,
    @LineItems dbo.InvoiceLineItemType READONLY,
    @UpdatedBy UNIQUEIDENTIFIER,
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRANSACTION;

    BEGIN TRY
        -- Get DocumentId and owner fields from Invoice if not provided
        DECLARE @DocumentId UNIQUEIDENTIFIER;
        SELECT
            @DocumentId = DocumentId,
            @OwnerCompanyId = ISNULL(@OwnerCompanyId, OwnerCompanyId),
            @OwnerWorkPlaceId = ISNULL(@OwnerWorkPlaceId, OwnerWorkPlaceId),
            @OwnerLocationId = ISNULL(@OwnerLocationId, OwnerLocationId)
        FROM dbo.Invoice
        WHERE Id = @InvoiceId;

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

        -- Update Invoice (now with contact/bank fields)
        EXEC dbo.sp_Invoice_Update
            @Id = @InvoiceId,
            @PartnerId = @PartnerId,
            @PartnerAddressSediuId = @PartnerAddressSediuId,
            @PartnerAddressCorespondentaId = @PartnerAddressCorespondentaId,
            @PartnerAddressLivrareId = @PartnerAddressLivrareId,
            @PartnerAddressFacturareId = @PartnerAddressFacturareId,
            @PartnerContactId = @PartnerContactId,
            @PartnerBankAccountId = @PartnerBankAccountId,
            @Observations = @InvoiceObservations,
            @UpdatedBy = @UpdatedBy;

        -- Update Line Items (both DocumentDetail and InvoiceDetail)
        EXEC dbo.sp_InvoiceDetail_UpdateLineItems
            @InvoiceId = @InvoiceId,
            @DocumentId = @DocumentId,
            @LineItems = @LineItems,
            @UpdatedBy = @UpdatedBy,
            @OwnerCompanyId = @OwnerCompanyId,
            @OwnerWorkPlaceId = @OwnerWorkPlaceId,
            @OwnerLocationId = @OwnerLocationId;

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

PRINT 'Fixed sp_Invoice_UpdateComplete to pass DocumentId and owner fields';

PRINT '=== Migration 057 completed - Detail table saving issue fixed ===';
GO
