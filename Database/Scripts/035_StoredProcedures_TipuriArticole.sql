PRINT '=== Începere creare stored procedures ItemTypes ===';

-- sp_ItemTypes_GetPaged - Paged query for ItemTypes grid
IF OBJECT_ID('dbo.sp_ItemTypes_GetPaged', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_ItemTypes_GetPaged;
GO
CREATE PROCEDURE dbo.sp_ItemTypes_GetPaged
    @SearchTerm NVARCHAR(100) = NULL,
    @SortField NVARCHAR(50) = 'CreatedAt',
    @SortDirection NVARCHAR(4) = 'DESC',
    @Skip INT = 0,
    @Take INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    -- Whitelist validation for sort field
    IF @SortField NOT IN ('ItemTypeCode', 'ItemTypeName', 'Description', 'IsActive', 'CreatedAt')
        SET @SortField = 'CreatedAt';

    -- Whitelist validation for sort direction
    IF @SortDirection NOT IN ('ASC', 'DESC')
        SET @SortDirection = 'DESC';

    DECLARE @SQL NVARCHAR(MAX);
    DECLARE @CountSQL NVARCHAR(MAX);
    DECLARE @WhereClause NVARCHAR(MAX) = N' WHERE 1=1 ';

    -- Search filter
    IF @SearchTerm IS NOT NULL
    BEGIN
        SET @WhereClause = @WhereClause + N' AND (ItemTypeCode LIKE ''%'' + @SearchTerm + ''%''
                                                    OR ItemTypeName LIKE ''%'' + @SearchTerm + ''%''
                                                    OR Description LIKE ''%'' + @SearchTerm + ''%'') ';
    END

    -- Build count query
    SET @CountSQL = N'SELECT COUNT(*) AS TotalCount FROM ItemTypes ' + @WhereClause;

    -- Build data query
    SET @SQL = N'SELECT Id, ItemTypeCode, ItemTypeName, Description, IsActive, CreatedAt, UpdatedAt
                 FROM ItemTypes ' + @WhereClause +
               N' ORDER BY ' + QUOTENAME(@SortField) + N' ' + @SortDirection +
               N' OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY';

    -- Execute data query (returns first result set with data)
    EXEC sp_executesql @SQL,
                       N'@SearchTerm NVARCHAR(100), @Skip INT, @Take INT',
                       @SearchTerm, @Skip, @Take;

    -- Execute count (returns second result set with count)
    EXEC sp_executesql @CountSQL,
                       N'@SearchTerm NVARCHAR(100)',
                       @SearchTerm;
END
GO

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
    @Description NVARCHAR(500) = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Id UNIQUEIDENTIFIER = NEWID();
    INSERT INTO dbo.ItemTypes (Id, ItemTypeCode, ItemTypeName, Description, CreatedAt, CreatedBy)
    VALUES (@Id, @ItemTypeCode, @ItemTypeName, @Description, GETDATE(), @CreatedBy);
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
    @Description NVARCHAR(500) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.ItemTypes
    SET ItemTypeCode = @ItemTypeCode,
        ItemTypeName = @ItemTypeName,
        Description = @Description,
        UpdatedAt = GETDATE(),
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
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT '✅ Stored procedures ItemTypes create/ update/ delete/ get complete';