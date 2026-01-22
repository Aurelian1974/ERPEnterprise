-- Test-DetailTablesSaving.sql
-- Script de testare pentru verificarea salvării în tabelele de detalii

PRINT '=== Start Test: Detail Tables Saving ===';
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

PRINT 'Test Data IDs:';
PRINT '  UserId: ' + CAST(@TestUserId AS NVARCHAR(50));
PRINT '  PartnerId: ' + CAST(@TestPartnerId AS NVARCHAR(50));
PRINT '  ItemId: ' + CAST(@TestItemId AS NVARCHAR(50));
PRINT '  CompanyId: ' + CAST(@TestCompanyId AS NVARCHAR(50));
PRINT '  WorkPlaceId: ' + CAST(@TestWorkPlaceId AS NVARCHAR(50));
PRINT '  LocationId: ' + CAST(@TestLocationId AS NVARCHAR(50));
PRINT '';

-- Check if we have test data
IF @TestUserId IS NULL OR @TestPartnerId IS NULL OR @TestItemId IS NULL
BEGIN
    PRINT 'ERROR: Missing test data (Users, Partners, or Articole)';
    PRINT 'Cannot run test without basic data';
    RETURN;
END

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
    <Item>
        <ItemId>' + CAST(@TestItemId AS NVARCHAR(50)) + '</ItemId>
        <Quantity>5.00</Quantity>
        <UnitMeasure>buc</UnitMeasure>
        <UnitPrice>200.00</UnitPrice>
        <VATRate>19.00</VATRate>
    </Item>
</LineItems>';

PRINT '=== Test 1: Create Purchase Invoice ===';
PRINT '';

BEGIN TRY
    -- Call the stored procedure to create an invoice
    DECLARE @DocumentId UNIQUEIDENTIFIER;
    DECLARE @InvoiceId UNIQUEIDENTIFIER;

    EXEC dbo.sp_Document_InsertPurchaseInvoice
        @DocumentDate = '2026-01-22',
        @DueDate = '2026-02-22',
        @DocumentNumber = 'TEST-FACTURA-001',
        @DocumentTypeCode = 'FFA',
        @DocumentStateCode = 'C',
        @Observations = 'Test invoice for detail tables',
        @UserId = @TestUserId,
        @EntityIntroduced = NULL,
        @PartnerId = @TestPartnerId,
        @PartnerAddressSediuId = NULL,
        @PartnerAddressCorespondentaId = NULL,
        @PartnerAddressLivrareId = NULL,
        @PartnerAddressFacturareId = NULL,
        @PartnerContactId = NULL,
        @PartnerBankAccountId = NULL,
        @OwnerCompanyId = @TestCompanyId,
        @OwnerWorkPlaceId = @TestWorkPlaceId,
        @OwnerLocationId = @TestLocationId,
        @InvoiceObservations = 'Test observations',
        @LineItems = @LineItemsXml;

    -- Get the created IDs
    SELECT TOP 1
        @DocumentId = DocumentId,
        @InvoiceId = InvoiceId
    FROM (
        SELECT TOP 1 i.DocumentId, i.Id as InvoiceId
        FROM Invoice i
        INNER JOIN Document d ON i.DocumentId = d.Id
        WHERE d.DocumentNumber = 'TEST-FACTURA-001'
        ORDER BY i.CreatedAt DESC
    ) AS LastInvoice;

    PRINT 'Invoice created successfully!';
    PRINT '  DocumentId: ' + CAST(@DocumentId AS NVARCHAR(50));
    PRINT '  InvoiceId: ' + CAST(@InvoiceId AS NVARCHAR(50));
    PRINT '';

    -- Check DocumentDetail records
    DECLARE @DocumentDetailCount INT;
    SELECT @DocumentDetailCount = COUNT(*)
    FROM dbo.DocumentDetail
    WHERE DocumentId = @DocumentId AND IsActive = 1;

    PRINT 'DocumentDetail records: ' + CAST(@DocumentDetailCount AS NVARCHAR(10));

    IF @DocumentDetailCount = 2
        PRINT '  ✓ PASS: DocumentDetail records created correctly';
    ELSE
        PRINT '  ✗ FAIL: Expected 2 DocumentDetail records, found ' + CAST(@DocumentDetailCount AS NVARCHAR(10));

    PRINT '';

    -- Check InvoiceDetail records
    DECLARE @InvoiceDetailCount INT;
    SELECT @InvoiceDetailCount = COUNT(*)
    FROM dbo.InvoiceDetail
    WHERE InvoiceId = @InvoiceId AND IsActive = 1;

    PRINT 'InvoiceDetail records: ' + CAST(@InvoiceDetailCount AS NVARCHAR(10));

    IF @InvoiceDetailCount = 2
        PRINT '  ✓ PASS: InvoiceDetail records created correctly';
    ELSE
        PRINT '  ✗ FAIL: Expected 2 InvoiceDetail records, found ' + CAST(@InvoiceDetailCount AS NVARCHAR(10));

    PRINT '';

    -- Show details
    PRINT 'DocumentDetail contents:';
    SELECT
        dd.Id,
        dd.ItemId,
        a.Denumire as ItemName,
        dd.Quantity,
        dd.UnitMeasure
    FROM dbo.DocumentDetail dd
    INNER JOIN dbo.Articole a ON dd.ItemId = a.Id
    WHERE dd.DocumentId = @DocumentId;

    PRINT '';
    PRINT 'InvoiceDetail contents:';
    SELECT
        id.Id,
        id.DocumentDetailId,
        id.UnitPrice,
        id.VATRate,
        id.VATAmount,
        id.LineTotal
    FROM dbo.InvoiceDetail id
    WHERE id.InvoiceId = @InvoiceId;

    PRINT '';

    -- Check invoice totals
    DECLARE @TotalAmount DECIMAL(18,2), @VATAmount DECIMAL(18,2), @TotalPayment DECIMAL(18,2);
    SELECT
        @TotalAmount = TotalAmount,
        @VATAmount = VATAmount,
        @TotalPayment = TotalPayment
    FROM dbo.Invoice
    WHERE Id = @InvoiceId;

    PRINT 'Invoice totals:';
    PRINT '  TotalAmount: ' + CAST(@TotalAmount AS NVARCHAR(20));
    PRINT '  VATAmount: ' + CAST(@VATAmount AS NVARCHAR(20));
    PRINT '  TotalPayment: ' + CAST(@TotalPayment AS NVARCHAR(20));

    -- Expected: (10 * 100) + (5 * 200) = 2000
    -- VAT: 2000 * 0.19 = 380
    -- Total: 2000 + 380 = 2380
    IF @TotalAmount = 2000.00 AND @VATAmount = 380.00 AND @TotalPayment = 2380.00
        PRINT '  ✓ PASS: Invoice totals calculated correctly';
    ELSE
        PRINT '  ✗ FAIL: Invoice totals incorrect (Expected: 2000, 380, 2380)';

    PRINT '';
    PRINT '=== Test 1: COMPLETED ===';
    PRINT '';

    -- Cleanup test data
    PRINT '=== Cleaning up test data ===';
    DELETE FROM dbo.InvoiceDetail WHERE InvoiceId = @InvoiceId;
    DELETE FROM dbo.DocumentDetail WHERE DocumentId = @DocumentId;
    DELETE FROM dbo.Invoice WHERE Id = @InvoiceId;
    DELETE FROM dbo.Document WHERE Id = @DocumentId;
    PRINT 'Test invoice deleted';
    PRINT '';

END TRY
BEGIN CATCH
    PRINT '✗ ERROR during test:';
    PRINT '  Error Message: ' + ERROR_MESSAGE();
    PRINT '  Error Line: ' + CAST(ERROR_LINE() AS NVARCHAR(10));
    PRINT '  Error Procedure: ' + ISNULL(ERROR_PROCEDURE(), 'N/A');

    -- Try to cleanup
    IF @DocumentId IS NOT NULL
    BEGIN
        DELETE FROM dbo.InvoiceDetail WHERE InvoiceId IN (SELECT Id FROM dbo.Invoice WHERE DocumentId = @DocumentId);
        DELETE FROM dbo.DocumentDetail WHERE DocumentId = @DocumentId;
        DELETE FROM dbo.Invoice WHERE DocumentId = @DocumentId;
        DELETE FROM dbo.Document WHERE Id = @DocumentId;
    END
END CATCH

PRINT '';
PRINT '=== Test Complete ===';
