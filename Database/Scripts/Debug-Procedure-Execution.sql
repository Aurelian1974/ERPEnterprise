-- Debug-Procedure-Execution.sql
-- Verifică dacă procedura sp_Document_InsertPurchaseInvoice procesează corect line items

PRINT '=== Debug: Procedure Execution with Manual Debugging ===';
PRINT '';

-- Variables for test
DECLARE @TestUserId UNIQUEIDENTIFIER;
DECLARE @TestPartnerId UNIQUEIDENTIFIER;
DECLARE @TestItemId UNIQUEIDENTIFIER;
DECLARE @TestCompanyId UNIQUEIDENTIFIER;
DECLARE @TestWorkPlaceId UNIQUEIDENTIFIER;
DECLARE @TestLocationId UNIQUEIDENTIFIER;

-- Get test data
SELECT TOP 1 @TestUserId = Id FROM dbo.Users WHERE IsActive = 1;
SELECT TOP 1 @TestPartnerId = Id FROM dbo.Partners WHERE IsActive = 1;
SELECT TOP 1 @TestItemId = Id FROM dbo.Articole WHERE IsActive = 1;
SELECT TOP 1 @TestCompanyId = Id FROM dbo.Company WHERE IsActive = 1;
SELECT TOP 1 @TestWorkPlaceId = Id FROM dbo.WorkPlace WHERE IsActive = 1;
SELECT TOP 1 @TestLocationId = Id FROM dbo.Location WHERE IsActive = 1;

-- Enable XACT_ABORT and NOCOUNT to match SP settings
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT 'Creating test invoice with manual steps...';
    PRINT '';

    -- Get DocumentStateId from code
    DECLARE @DocumentStateId UNIQUEIDENTIFIER;
    SELECT @DocumentStateId = Id FROM dbo.DocumentState WHERE CodStare = 'C' AND IsActive = 1;

    PRINT 'DocumentStateId: ' + CAST(@DocumentStateId AS NVARCHAR(50));

    -- Insert Document
    DECLARE @DocumentId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO dbo.Document (
        Id, DocumentDate, DueDate, DocumentNumber, DocumentTypeCode,
        DocumentStateId, Observations, UserId, EntityIntroduced,
        CreatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
    ) VALUES (
        @DocumentId, '2026-01-22', '2026-02-22', 'DEBUG-TEST-001', 'FFA',
        @DocumentStateId, 'Debug test', @TestUserId, NULL,
        @TestUserId, @TestCompanyId, @TestWorkPlaceId, @TestLocationId
    );

    PRINT 'Document inserted: ' + CAST(@DocumentId AS NVARCHAR(50));

    -- Insert Invoice
    DECLARE @InvoiceId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO dbo.Invoice (
        Id, DocumentId, PartnerId, Observations, UserId,
        CreatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId,
        PartnerAddressSediuId, PartnerAddressCorespondentaId, PartnerAddressLivrareId, PartnerAddressFacturareId
    ) VALUES (
        @InvoiceId, @DocumentId, @TestPartnerId, 'Debug test', @TestUserId,
        @TestUserId, @TestCompanyId, @TestWorkPlaceId, @TestLocationId,
        NULL, NULL, NULL, NULL
    );

    PRINT 'Invoice inserted: ' + CAST(@InvoiceId AS NVARCHAR(50));
    PRINT '';

    -- Build XML for line items
    DECLARE @LineItemsXml XML = N'
<LineItems>
    <Item>
        <ItemId>' + CAST(@TestItemId AS NVARCHAR(50)) + '</ItemId>
        <Quantity>10.00</Quantity>
        <UnitMeasure>buc</UnitMeasure>
        <UnitPrice>100.00</UnitPrice>
        <VATRate>19.00</VATRate>
    </Item>
</LineItems>';

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

    PRINT 'Parsing XML...';
    INSERT INTO @LineItem (ItemId, Quantity, UnitMeasure, UnitPrice, VATRate)
    SELECT
        Item.value('(ItemId)[1]', 'UNIQUEIDENTIFIER'),
        Item.value('(Quantity)[1]', 'DECIMAL(18,2)'),
        Item.value('(UnitMeasure)[1]', 'NVARCHAR(20)'),
        Item.value('(UnitPrice)[1]', 'DECIMAL(18,2)'),
        Item.value('(VATRate)[1]', 'DECIMAL(5,2)')
    FROM @LineItemsXml.nodes('/LineItems/Item') AS Items(Item);

    DECLARE @LineItemCount INT;
    SELECT @LineItemCount = COUNT(*) FROM @LineItem;
    PRINT 'Parsed line items: ' + CAST(@LineItemCount AS NVARCHAR(10));

    IF @LineItemCount = 0
    BEGIN
        PRINT '✗ ERROR: XML parsing failed - no items parsed!';
        ROLLBACK TRANSACTION;
        RETURN;
    END

    SELECT * FROM @LineItem;
    PRINT '';

    -- Insert DocumentDetail and InvoiceDetail
    DECLARE @ItemId UNIQUEIDENTIFIER, @Quantity DECIMAL(18,2), @UnitMeasure NVARCHAR(20),
            @UnitPrice DECIMAL(18,2), @VATRate DECIMAL(5,2);

    PRINT 'Processing line items with cursor...';
    DECLARE line_cursor CURSOR FOR
    SELECT ItemId, Quantity, UnitMeasure, UnitPrice, VATRate FROM @LineItem;

    OPEN line_cursor;
    FETCH NEXT FROM line_cursor INTO @ItemId, @Quantity, @UnitMeasure, @UnitPrice, @VATRate;

    DECLARE @LoopCount INT = 0;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @LoopCount = @LoopCount + 1;
        PRINT 'Processing item ' + CAST(@LoopCount AS NVARCHAR(10)) + ': ' + CAST(@ItemId AS NVARCHAR(50));

        -- Insert DocumentDetail
        DECLARE @DocumentDetailId UNIQUEIDENTIFIER = NEWID();
        INSERT INTO dbo.DocumentDetail (
            Id, DocumentId, ItemId, Quantity, UnitMeasure,
            CreatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
        ) VALUES (
            @DocumentDetailId, @DocumentId, @ItemId, @Quantity, @UnitMeasure,
            @TestUserId, @TestCompanyId, @TestWorkPlaceId, @TestLocationId
        );

        PRINT '  DocumentDetail inserted: ' + CAST(@DocumentDetailId AS NVARCHAR(50));

        -- Calculate line totals
        DECLARE @LineTotal DECIMAL(18,2) = @Quantity * @UnitPrice;
        DECLARE @VATAmount DECIMAL(18,2) = @LineTotal * (@VATRate / 100);

        -- Insert InvoiceDetail
        INSERT INTO dbo.InvoiceDetail (
            Id, InvoiceId, DocumentDetailId, UnitPrice, VATRate, VATAmount, LineTotal,
            CreatedBy, OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
        ) VALUES (
            NEWID(), @InvoiceId, @DocumentDetailId, @UnitPrice, @VATRate, @VATAmount, @LineTotal + @VATAmount,
            @TestUserId, @TestCompanyId, @TestWorkPlaceId, @TestLocationId
        );

        PRINT '  InvoiceDetail inserted';
        PRINT '  LineTotal: ' + CAST(@LineTotal AS NVARCHAR(20)) + ', VATAmount: ' + CAST(@VATAmount AS NVARCHAR(20));

        -- Accumulate totals
        SET @TotalAmount = @TotalAmount + @LineTotal;
        SET @TotalVAT = @TotalVAT + @VATAmount;

        FETCH NEXT FROM line_cursor INTO @ItemId, @Quantity, @UnitMeasure, @UnitPrice, @VATRate;
    END

    CLOSE line_cursor;
    DEALLOCATE line_cursor;

    PRINT '';
    PRINT 'Cursor processed ' + CAST(@LoopCount AS NVARCHAR(10)) + ' items';
    PRINT 'Total Amount: ' + CAST(@TotalAmount AS NVARCHAR(20));
    PRINT 'Total VAT: ' + CAST(@TotalVAT AS NVARCHAR(20));
    PRINT '';

    -- Update Invoice totals
    UPDATE dbo.Invoice
    SET TotalAmount = @TotalAmount,
        VATAmount = @TotalVAT,
        TotalPayment = @TotalAmount + @TotalVAT,
        UpdatedAt = GETDATE(),
        UpdatedBy = @TestUserId
    WHERE Id = @InvoiceId;

    PRINT 'Invoice totals updated';
    PRINT '';

    -- Verify results
    PRINT 'Verification:';
    SELECT COUNT(*) as DocumentDetailCount FROM dbo.DocumentDetail WHERE DocumentId = @DocumentId;
    SELECT COUNT(*) as InvoiceDetailCount FROM dbo.InvoiceDetail WHERE InvoiceId = @InvoiceId;
    SELECT TotalAmount, VATAmount, TotalPayment FROM dbo.Invoice WHERE Id = @InvoiceId;

    COMMIT TRANSACTION;
    PRINT '';
    PRINT '✓ Transaction committed successfully';
    PRINT '';

    -- Cleanup
    PRINT 'Cleaning up...';
    DELETE FROM dbo.InvoiceDetail WHERE InvoiceId = @InvoiceId;
    DELETE FROM dbo.DocumentDetail WHERE DocumentId = @DocumentId;
    DELETE FROM dbo.Invoice WHERE Id = @InvoiceId;
    DELETE FROM dbo.Document WHERE Id = @DocumentId;
    PRINT 'Cleanup complete';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT '';
    PRINT '✗ ERROR:';
    PRINT '  Message: ' + ERROR_MESSAGE();
    PRINT '  Line: ' + CAST(ERROR_LINE() AS NVARCHAR(10));
    PRINT '  Procedure: ' + ISNULL(ERROR_PROCEDURE(), 'N/A');
    PRINT '  Severity: ' + CAST(ERROR_SEVERITY() AS NVARCHAR(10));
    PRINT '  State: ' + CAST(ERROR_STATE() AS NVARCHAR(10));
END CATCH

PRINT '';
PRINT '=== Debug Complete ===';
