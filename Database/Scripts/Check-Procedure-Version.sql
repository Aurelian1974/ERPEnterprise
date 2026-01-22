-- Check-Procedure-Version.sql
-- Verifică versiunea și conținutul procedurii sp_Document_InsertPurchaseInvoice

PRINT '=== Checking sp_Document_InsertPurchaseInvoice ===';
PRINT '';

-- Check if procedure exists
IF OBJECT_ID('dbo.sp_Document_InsertPurchaseInvoice', 'P') IS NULL
BEGIN
    PRINT '✗ ERROR: Procedure sp_Document_InsertPurchaseInvoice does NOT exist!';
    RETURN;
END
ELSE
BEGIN
    PRINT '✓ Procedure sp_Document_InsertPurchaseInvoice exists';
END

PRINT '';

-- Get procedure definition
PRINT 'Procedure definition:';
PRINT '================================================';
SELECT OBJECT_DEFINITION(OBJECT_ID('dbo.sp_Document_InsertPurchaseInvoice'));
PRINT '================================================';
PRINT '';

-- Get procedure metadata
SELECT
    name,
    type_desc,
    create_date,
    modify_date
FROM sys.objects
WHERE name = 'sp_Document_InsertPurchaseInvoice';

PRINT '';

-- Check if procedure has the correct parameters
SELECT
    p.name AS ProcedureName,
    par.name AS ParameterName,
    t.name AS ParameterType,
    par.max_length,
    par.is_output
FROM sys.procedures p
INNER JOIN sys.parameters par ON p.object_id = par.object_id
INNER JOIN sys.types t ON par.system_type_id = t.system_type_id
WHERE p.name = 'sp_Document_InsertPurchaseInvoice'
ORDER BY par.parameter_id;

PRINT '';
PRINT '=== Check Complete ===';
