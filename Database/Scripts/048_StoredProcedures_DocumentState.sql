PRINT '=== Începere migrare 048_StoredProcedures_DocumentState ===';

-- =============================================
-- DocumentState Stored Procedures
-- =============================================

-- sp_DocumentState_GetAll
IF OBJECT_ID('dbo.sp_DocumentState_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_DocumentState_GetAll;
GO

CREATE PROCEDURE dbo.sp_DocumentState_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        Id,
        CodStare,
        DenumireStare,
        Descriere,
        IsActive,
        CreatedAt,
        UpdatedAt
    FROM dbo.DocumentState
    WHERE IsActive = 1
    ORDER BY CodStare;
END
GO

PRINT '✅ Stored procedures pentru DocumentState create';

PRINT '=== Migrare 048_StoredProcedures_DocumentState completă ===';