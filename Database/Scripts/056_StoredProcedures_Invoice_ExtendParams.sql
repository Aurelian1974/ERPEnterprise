-- 056_StoredProcedures_Invoice_ExtendParams.sql
-- Add PartnerContactId and PartnerBankAccountId parameters to master SPs
-- and persist them inline to dbo.Invoice

PRINT '=== Begin migration 056_StoredProcedures_Invoice_ExtendParams ===';

-- Ensure session options
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Drop and recreate sp_Document_InsertPurchaseInvoice with new params
IF OBJECT_ID('dbo.sp_Document_InsertPurchaseInvoice', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Document_InsertPurchaseInvoice;
GO

CREATE PROCEDURE dbo.sp_Document_InsertPurchaseInvoice
    @DocumentDate DATE,
    @DueDate DATE = NULL,
    @DocumentNumber NVARCHAR(50),
    @DocumentTypeCode NVARCHAR(10) = 'FFA',
    @DocumentStateCode NVARCHAR(10) = 'C',
    @Observations NVARCHAR(500) = NULL,
    @UserId UNIQUEIDENTIFIER,
    @EntityIntroduced UNIQUEIDENTIFIER = NULL,
    @PartnerId UNIQUEIDENTIFIER,
    @PartnerAddressSediuId UNIQUEIDENTIFIER = NULL,
    @PartnerAddressCorespondentaId UNIQUEIDENTIFIER = NULL,
    @PartnerAddressLivrareId UNIQUEIDENTIFIER = NULL,
    @PartnerAddressFacturareId UNIQUEIDENTIFIER = NULL,
    @PartnerContactId UNIQUEIDENTIFIER = NULL,
    @PartnerBankAccountId UNIQUEIDENTIFIER = NULL,
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL,
    @InvoiceObservations NVARCHAR(500) = NULL,
    -- XML parameter for line items
    @LineItems XML
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    SET QUOTED_IDENTIFIER ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Get DocumentStateId from code
        DECLARE @DocumentStateId UNIQUEIDENTIFIER;
        SELECT @DocumentStateId = Id FROM dbo.DocumentState WHERE CodStare = @DocumentStateCode AND IsActive = 1;

        IF @DocumentStateId IS NULL
        BEGIN
            RAISERROR('Invalid DocumentStateCode: %s', 16, 1, @DocumentStateCode);
            RETURN;
        END

        -- Insert Document
        DECLARE @DocumentId UNIQUEIDENTIFIER = NEWID();
        INSERT INTO dbo.Document (
            Id, DocumentDate, DueDate, DocumentNumber, DocumentTypeCode,
            DocumentStateId, Observations, UserId, EntityIntroduced,
            CreatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
        ) VALUES (
            @DocumentId, @DocumentDate, @DueDate, @DocumentNumber, @DocumentTypeCode,
            @DocumentStateId, @Observations, @UserId, @EntityIntroduced,
            @UserId, @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId
        );

        -- Insert Invoice (now persisting partner contact and bank account)
        DECLARE @InvoiceId UNIQUEIDENTIFIER = NEWID();
        INSERT INTO dbo.Invoice (
            Id, DocumentId, PartnerId, Observations, UserId,
            CreatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId,
            PartnerAddressSediuId, PartnerAddressCorespondentaId, PartnerAddressLivrareId, PartnerAddressFacturareId,
            PartnerContactId, PartnerBankAccountId
        ) VALUES (
            @InvoiceId, @DocumentId, @PartnerId, @InvoiceObservations, @UserId,
            @UserId, @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId,
            @PartnerAddressSediuId, @PartnerAddressCorespondentaId, @PartnerAddressLivrareId, @PartnerAddressFacturareId,
            @PartnerContactId, @PartnerBankAccountId
        );

        -- Variables for totals
        DECLARE @TotalAmount DECIMAL(18,2) = 0;
        DECLARE @TotalVAT DECIMAL(18,2) = 0;

        -- Process line items from XML
        DECLARE @LineItem TABLE (
            ItemId UNIQUEIDENTIFIER,
            Quantity DECIMAL(18,2),
            UnitMeasure NVARCHAR(20),
            UnitPrice DECIMAL(18,2),
            VATRate DECIMAL(5,2)
        );

        INSERT INTO @LineItem (ItemId, Quantity, UnitMeasure, UnitPrice, VATRate)
        SELECT
            Item.value('(ItemId)[1]', 'UNIQUEIDENTIFIER'),
            Item.value('(Quantity)[1]', 'DECIMAL(18,2)'),
            Item.value('(UnitMeasure)[1]', 'NVARCHAR(20)'),
            Item.value('(UnitPrice)[1]', 'DECIMAL(18,2)'),
            Item.value('(VATRate)[1]', 'DECIMAL(5,2)')
        FROM @LineItems.nodes('/LineItems/Item') AS Items(Item);

        -- Insert DocumentDetail and InvoiceDetail
        DECLARE @ItemId UNIQUEIDENTIFIER, @Quantity DECIMAL(18,2), @UnitMeasure NVARCHAR(20),
                @UnitPrice DECIMAL(18,2), @VATRate DECIMAL(5,2);

        DECLARE line_cursor CURSOR FOR
        SELECT ItemId, Quantity, UnitMeasure, UnitPrice, VATRate FROM @LineItem;

        OPEN line_cursor;
        FETCH NEXT FROM line_cursor INTO @ItemId, @Quantity, @UnitMeasure, @UnitPrice, @VATRate;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            -- Insert DocumentDetail
            DECLARE @DocumentDetailId UNIQUEIDENTIFIER = NEWID();
            INSERT INTO dbo.DocumentDetail (
                Id, DocumentId, ItemId, Quantity, UnitMeasure,
                CreatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
            ) VALUES (
                @DocumentDetailId, @DocumentId, @ItemId, @Quantity, @UnitMeasure,
                @UserId, @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId
            );

            -- Calculate line totals
            DECLARE @LineTotal DECIMAL(18,2) = @Quantity * @UnitPrice;
            DECLARE @VATAmount DECIMAL(18,2) = @LineTotal * (@VATRate / 100);

            -- Insert InvoiceDetail
            INSERT INTO dbo.InvoiceDetail (
                Id, InvoiceId, DocumentDetailId, UnitPrice, VATRate, VATAmount, LineTotal,
                CreatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
            ) VALUES (
                NEWID(), @InvoiceId, @DocumentDetailId, @UnitPrice, @VATRate, @VATAmount, @LineTotal + @VATAmount,
                @UserId, @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId
            );

            -- Accumulate totals
            SET @TotalAmount = @TotalAmount + @LineTotal;
            SET @TotalVAT = @TotalVAT + @VATAmount;

            -- Update stock if item is stockable and document is valid
            IF @DocumentStateCode = 'V'
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.Articole WHERE Id = @ItemId AND IsStockable = 1)
                BEGIN
                    EXEC dbo.sp_Stock_UpdateQuantity @ItemId, @OwnerLocationId, @Quantity, @UserId, @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId;
                END
            END

            FETCH NEXT FROM line_cursor INTO @ItemId, @Quantity, @UnitMeasure, @UnitPrice, @VATRate;
        END

        CLOSE line_cursor;
        DEALLOCATE line_cursor;

        -- Update Invoice totals
        UPDATE dbo.Invoice
        SET TotalAmount = @TotalAmount,
            VATAmount = @TotalVAT,
            TotalPayment = @TotalAmount + @TotalVAT,
            UpdatedAt = GETDATE(),
            UpdatedBy = @UserId
        WHERE Id = @InvoiceId;

        COMMIT TRANSACTION;

        -- Return the created IDs
        SELECT @DocumentId as DocumentId, @InvoiceId as InvoiceId;

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        THROW;
    END CATCH
END
GO

PRINT 'Recreated sp_Document_InsertPurchaseInvoice with partner contact/bank params';

-- Update sp_Invoice_Update to include contact and bank account
IF OBJECT_ID('dbo.sp_Invoice_Update', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Invoice_Update;
GO

CREATE PROCEDURE dbo.sp_Invoice_Update
    @Id UNIQUEIDENTIFIER,
    @PartnerId UNIQUEIDENTIFIER,
    @PartnerAddressSediuId UNIQUEIDENTIFIER = NULL,
    @PartnerAddressCorespondentaId UNIQUEIDENTIFIER = NULL,
    @PartnerAddressLivrareId UNIQUEIDENTIFIER = NULL,
    @PartnerAddressFacturareId UNIQUEIDENTIFIER = NULL,
    @PartnerContactId UNIQUEIDENTIFIER = NULL,
    @PartnerBankAccountId UNIQUEIDENTIFIER = NULL,
    @Observations NVARCHAR(500) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Invoice
    SET
        PartnerId = @PartnerId,
        PartnerAddressSediuId = @PartnerAddressSediuId,
        PartnerAddressCorespondentaId = @PartnerAddressCorespondentaId,
        PartnerAddressLivrareId = @PartnerAddressLivrareId,
        PartnerAddressFacturareId = @PartnerAddressFacturareId,
        PartnerContactId = @PartnerContactId,
        PartnerBankAccountId = @PartnerBankAccountId,
        Observations = @Observations,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;

    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT 'Recreated sp_Invoice_Update with contact/bank params';

-- Recreate sp_Invoice_UpdateComplete to pass through the new params
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

PRINT 'Recreated sp_Invoice_UpdateComplete to include contact/bank params';

-- Recreate sp_InvoiceDetail_UpdateLineItems with DocumentId and owner params
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
        -- Insert DocumentDetail first
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

        -- Insert InvoiceDetail with reference to DocumentDetail
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

PRINT 'Recreated sp_InvoiceDetail_UpdateLineItems with DocumentId and owner params';

PRINT '=== Migration 056 completed ===';
GO
