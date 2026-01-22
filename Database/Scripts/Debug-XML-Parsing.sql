-- Debug-XML-Parsing.sql
-- Script pentru debugging procesarea XML în sp_Document_InsertPurchaseInvoice

PRINT '=== Debug: XML Parsing ===';
PRINT '';

-- Get test item
DECLARE @TestItemId UNIQUEIDENTIFIER;
SELECT TOP 1 @TestItemId = Id FROM dbo.Articole WHERE IsActive = 1;

PRINT 'Test ItemId: ' + CAST(@TestItemId AS NVARCHAR(50));
PRINT '';

-- Build the same XML as in the test
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

PRINT 'Generated XML:';
PRINT CAST(@LineItemsXml AS NVARCHAR(MAX));
PRINT '';

-- Test parsing the XML (same logic as in SP)
DECLARE @LineItem TABLE (
    ItemId UNIQUEIDENTIFIER,
    Quantity DECIMAL(18,2),
    UnitMeasure NVARCHAR(20),
    UnitPrice DECIMAL(18,2),
    VATRate DECIMAL(5,2)
);

PRINT 'Parsing XML into table variable...';
INSERT INTO @LineItem (ItemId, Quantity, UnitMeasure, UnitPrice, VATRate)
SELECT
    Item.value('(ItemId)[1]', 'UNIQUEIDENTIFIER'),
    Item.value('(Quantity)[1]', 'DECIMAL(18,2)'),
    Item.value('(UnitMeasure)[1]', 'NVARCHAR(20)'),
    Item.value('(UnitPrice)[1]', 'DECIMAL(18,2)'),
    Item.value('(VATRate)[1]', 'DECIMAL(5,2)')
FROM @LineItemsXml.nodes('/LineItems/Item') AS Items(Item);

DECLARE @ParsedCount INT;
SELECT @ParsedCount = COUNT(*) FROM @LineItem;

PRINT 'Parsed rows: ' + CAST(@ParsedCount AS NVARCHAR(10));
PRINT '';

IF @ParsedCount > 0
BEGIN
    PRINT 'Parsed data:';
    SELECT * FROM @LineItem;
    PRINT '';
    PRINT '✓ XML parsing works correctly!';
END
ELSE
BEGIN
    PRINT '✗ XML parsing FAILED - no rows parsed!';
    PRINT '';
    PRINT 'Trying different XML format...';

    -- Try without UNIQUEIDENTIFIER cast
    DELETE FROM @LineItem;

    INSERT INTO @LineItem (ItemId, Quantity, UnitMeasure, UnitPrice, VATRate)
    SELECT
        CAST(Item.value('(ItemId)[1]', 'NVARCHAR(50)') AS UNIQUEIDENTIFIER),
        Item.value('(Quantity)[1]', 'DECIMAL(18,2)'),
        Item.value('(UnitMeasure)[1]', 'NVARCHAR(20)'),
        Item.value('(UnitPrice)[1]', 'DECIMAL(18,2)'),
        Item.value('(VATRate)[1]', 'DECIMAL(5,2)')
    FROM @LineItemsXml.nodes('/LineItems/Item') AS Items(Item);

    SELECT @ParsedCount = COUNT(*) FROM @LineItem;
    PRINT 'Parsed rows with CAST: ' + CAST(@ParsedCount AS NVARCHAR(10));

    IF @ParsedCount > 0
    BEGIN
        SELECT * FROM @LineItem;
        PRINT '✓ XML parsing works with explicit CAST!';
    END
END

PRINT '';
PRINT '=== Debug Complete ===';
