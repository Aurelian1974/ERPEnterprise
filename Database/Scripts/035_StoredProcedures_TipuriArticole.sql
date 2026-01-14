PRINT '=== Începere creare stored procedures ItemTypes ===';

IF OBJECT_ID('dbo.sp_ItemTypes_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ItemTypes_GetAll;
GO
CREATE PROCEDURE dbo.sp_ItemTypes_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.ItemTypes WHERE IsActive = 1 ORDER BY ItemTypeName;
END
GO

IF OBJECT_ID('dbo.sp_ItemTypes_GetById', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ItemTypes_GetById;
GO
CREATE PROCEDURE dbo.sp_ItemTypes_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT * FROM dbo.ItemTypes WHERE Id = @Id;
END
GO

IF OBJECT_ID('dbo.sp_ItemTypes_Create', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ItemTypes_Create;
GO
CREATE PROCEDURE dbo.sp_ItemTypes_Create
    @ItemTypeCode NVARCHAR(50),
    @ItemTypeName NVARCHAR(200),
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id UNIQUEIDENTIFIER = NEWID();
    INSERT INTO dbo.ItemTypes (Id, ItemTypeCode, ItemTypeName, CreatedAt, CreatedBy)
    VALUES (@Id, @ItemTypeCode, @ItemTypeName, CAST(SYSUTCDATETIME() AT TIME ZONE 'E. Europe Standard Time' AS datetime2), @CreatedBy);
    SELECT @Id AS Id;
END
GO

IF OBJECT_ID('dbo.sp_ItemTypes_Update', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ItemTypes_Update;
GO
CREATE PROCEDURE dbo.sp_ItemTypes_Update
    @Id UNIQUEIDENTIFIER,
    @ItemTypeCode NVARCHAR(50),
    @ItemTypeName NVARCHAR(200),
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ItemTypes
    SET ItemTypeCode = @ItemTypeCode,
        ItemTypeName = @ItemTypeName,
        UpdatedAt = CAST(SYSUTCDATETIME() AT TIME ZONE 'E. Europe Standard Time' AS datetime2),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

IF OBJECT_ID('dbo.sp_ItemTypes_Delete', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ItemTypes_Delete;
GO
CREATE PROCEDURE dbo.sp_ItemTypes_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ItemTypes
    SET IsActive = 0,
        UpdatedAt = CAST(SYSUTCDATETIME() AT TIME ZONE 'E. Europe Standard Time' AS datetime2),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT '✅ Stored procedures ItemTypes create/ update/ delete/ get complete';