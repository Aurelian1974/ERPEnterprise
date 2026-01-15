PRINT '=== Începere creare stored procedures TipuriDocumente ===';

-- sp_TipuriDocumente_GetAll - Get all active document types
IF OBJECT_ID('dbo.sp_TipuriDocumente_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_TipuriDocumente_GetAll;
GO

CREATE PROCEDURE dbo.sp_TipuriDocumente_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, TipDocumentCode, TipDocumentName, Description, IsActive, CreatedAt, UpdatedAt
    FROM dbo.TipuriDocumente
    ORDER BY TipDocumentName;
END
GO

-- sp_TipuriDocumente_GetById - Get document type by ID
IF OBJECT_ID('dbo.sp_TipuriDocumente_GetById', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_TipuriDocumente_GetById;
GO

CREATE PROCEDURE dbo.sp_TipuriDocumente_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Id, TipDocumentCode, TipDocumentName, Description, IsActive, CreatedAt, UpdatedAt
    FROM dbo.TipuriDocumente
    WHERE Id = @Id;
END
GO

-- sp_TipuriDocumente_Create - Create new document type
IF OBJECT_ID('dbo.sp_TipuriDocumente_Create', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_TipuriDocumente_Create;
GO

CREATE PROCEDURE dbo.sp_TipuriDocumente_Create
    @TipDocumentCode NVARCHAR(10),
    @TipDocumentName NVARCHAR(100),
    @Description NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.TipuriDocumente (TipDocumentCode, TipDocumentName, Description, CreatedAt)
    VALUES (@TipDocumentCode, @TipDocumentName, @Description, GETDATE());

    SELECT SCOPE_IDENTITY() AS NewId;
END
GO

-- sp_TipuriDocumente_Update - Update document type
IF OBJECT_ID('dbo.sp_TipuriDocumente_Update', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_TipuriDocumente_Update;
GO

CREATE PROCEDURE dbo.sp_TipuriDocumente_Update
    @Id UNIQUEIDENTIFIER,
    @TipDocumentCode NVARCHAR(10),
    @TipDocumentName NVARCHAR(100),
    @Description NVARCHAR(500) = NULL,
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TipuriDocumente
    SET TipDocumentCode = @TipDocumentCode,
        TipDocumentName = @TipDocumentName,
        Description = @Description,
        IsActive = @IsActive,
        UpdatedAt = GETDATE()
    WHERE Id = @Id;
END
GO

-- sp_TipuriDocumente_Delete - Soft delete document type (set inactive)
IF OBJECT_ID('dbo.sp_TipuriDocumente_Delete', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_TipuriDocumente_Delete;
GO

CREATE PROCEDURE dbo.sp_TipuriDocumente_Delete
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TipuriDocumente
    SET IsActive = 0,
        UpdatedAt = GETDATE()
    WHERE Id = @Id;
END
GO

-- sp_TipuriDocumente_ExistsByCode - Check if code exists
IF OBJECT_ID('dbo.sp_TipuriDocumente_ExistsByCode', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_TipuriDocumente_ExistsByCode;
GO

CREATE PROCEDURE dbo.sp_TipuriDocumente_ExistsByCode
    @Code NVARCHAR(10),
    @ExcludeId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(*)
    FROM dbo.TipuriDocumente
    WHERE TipDocumentCode = @Code
    AND (@ExcludeId IS NULL OR Id != @ExcludeId);
END
GO

-- sp_TipuriDocumente_ExistsByName - Check if name exists
IF OBJECT_ID('dbo.sp_TipuriDocumente_ExistsByName', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_TipuriDocumente_ExistsByName;
GO

CREATE PROCEDURE dbo.sp_TipuriDocumente_ExistsByName
    @Name NVARCHAR(100),
    @ExcludeId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COUNT(*)
    FROM dbo.TipuriDocumente
    WHERE TipDocumentName = @Name
    AND (@ExcludeId IS NULL OR Id != @ExcludeId);
END
GO

PRINT '✅ Stored procedures pentru TipuriDocumente create';

PRINT '=== Stored procedures TipuriDocumente complet ===';