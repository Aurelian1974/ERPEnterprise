PRINT '=== Începere creare stored procedures Articole ===';

-- sp_Articole_GetPaged - Paged query for Articole grid with JOIN to ItemTypes
IF OBJECT_ID('dbo.sp_Articole_GetPaged', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Articole_GetPaged;
GO
CREATE PROCEDURE dbo.sp_Articole_GetPaged
    @SearchTerm NVARCHAR(100) = NULL,
    @SortField NVARCHAR(50) = 'CreatedAt',
    @SortDirection NVARCHAR(4) = 'DESC',
    @Skip INT = 0,
    @Take INT = 20
AS
BEGIN
    SET NOCOUNT ON;

    -- Whitelist validation for sort field
    IF @SortField NOT IN ('ArticolCode', 'ArticolName', 'Description', 'TipArticolName', 'UnitateMasura', 'PretAchizitie', 'PretVanzare', 'StocMinim', 'StocMaxim', 'IsActive', 'CreatedAt')
        SET @SortField = 'CreatedAt';

    -- Whitelist validation for sort direction
    IF @SortDirection NOT IN ('ASC', 'DESC')
        SET @SortDirection = 'DESC';

    DECLARE @SQL NVARCHAR(MAX);
    DECLARE @CountSQL NVARCHAR(MAX);
    DECLARE @WhereClause NVARCHAR(MAX) = N' WHERE a.IsActive = 1 ';

    -- Search filter
    IF @SearchTerm IS NOT NULL
    BEGIN
        SET @WhereClause = @WhereClause + N' AND (a.ArticolCode LIKE ''%'' + @SearchTerm + ''%''
                                                    OR a.ArticolName LIKE ''%'' + @SearchTerm + ''%''
                                                    OR a.Description LIKE ''%'' + @SearchTerm + ''%''
                                                    OR it.ItemTypeName LIKE ''%'' + @SearchTerm + ''%'') ';
    END

    -- Build count query
    SET @CountSQL = N'SELECT COUNT(*) AS TotalCount FROM Articole a
                     INNER JOIN ItemTypes it ON a.TipArticolId = it.Id ' + @WhereClause;

    -- Build data query with JOIN
    SET @SQL = N'SELECT a.Id, a.ArticolCode, a.ArticolName, a.Description,
                        a.TipArticolId, it.ItemTypeName AS TipArticolName,
                        a.UnitateMasura, a.PretAchizitie, a.PretVanzare,
                        a.StocMinim, a.StocMaxim, a.IsActive, a.CreatedAt, a.UpdatedAt
                 FROM Articole a
                 INNER JOIN ItemTypes it ON a.TipArticolId = it.Id ' + @WhereClause +
               N' ORDER BY ' + CASE
                   WHEN @SortField = 'TipArticolName' THEN N'it.ItemTypeName'
                   ELSE N'a.' + QUOTENAME(@SortField)
               END + N' ' + @SortDirection +
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

IF OBJECT_ID('dbo.sp_Articole_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Articole_GetAll;
GO
CREATE PROCEDURE dbo.sp_Articole_GetAll
AS
BEGIN
    SET NOCOUNT ON;
    SELECT a.Id, a.ArticolCode, a.ArticolName, a.Description,
           a.TipArticolId, it.ItemTypeName AS TipArticolName,
           a.UnitateMasura, a.PretAchizitie, a.PretVanzare,
           a.StocMinim, a.StocMaxim, a.IsStockable, a.IsActive, a.CreatedAt, a.UpdatedAt
    FROM dbo.Articole a
    INNER JOIN dbo.ItemTypes it ON a.TipArticolId = it.Id
    WHERE a.IsActive = 1
    ORDER BY a.ArticolName;
END
GO

IF OBJECT_ID('dbo.sp_Articole_GetById', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Articole_GetById;
GO
CREATE PROCEDURE dbo.sp_Articole_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT a.Id, a.ArticolCode, a.ArticolName, a.Description,
           a.TipArticolId, it.ItemTypeName AS TipArticolName,
           a.UnitateMasura, a.PretAchizitie, a.PretVanzare,
           a.StocMinim, a.StocMaxim, a.IsStockable, a.IsActive, a.CreatedAt, a.UpdatedAt
    FROM dbo.Articole a
    INNER JOIN dbo.ItemTypes it ON a.TipArticolId = it.Id
    WHERE a.Id = @Id;
END
GO

IF OBJECT_ID('dbo.sp_Articole_GetByCode', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Articole_GetByCode;
GO
CREATE PROCEDURE dbo.sp_Articole_GetByCode
    @ArticolCode NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SELECT a.Id, a.ArticolCode, a.ArticolName, a.Description,
           a.TipArticolId, it.ItemTypeName AS TipArticolName,
           a.UnitateMasura, a.PretAchizitie, a.PretVanzare,
           a.StocMinim, a.StocMaxim, a.IsStockable, a.IsActive, a.CreatedAt, a.UpdatedAt
    FROM dbo.Articole a
    INNER JOIN dbo.ItemTypes it ON a.TipArticolId = it.Id
    WHERE a.ArticolCode = @ArticolCode AND a.IsActive = 1;
END
GO

IF OBJECT_ID('dbo.sp_Articole_Create', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Articole_Create;
GO
CREATE PROCEDURE dbo.sp_Articole_Create
    @ArticolCode NVARCHAR(50),
    @ArticolName NVARCHAR(200),
    @Description NVARCHAR(500) = NULL,
    @TipArticolId UNIQUEIDENTIFIER,
    @UnitateMasura NVARCHAR(20) = 'BUC',
    @PretAchizitie DECIMAL(18,2) = NULL,
    @PretVanzare DECIMAL(18,2) = NULL,
    @StocMinim DECIMAL(18,2) = 0,
    @StocMaxim DECIMAL(18,2) = NULL,
    @IsStockable BIT = 1,
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Validate TipArticolId exists and is active
    IF NOT EXISTS (SELECT 1 FROM dbo.ItemTypes WHERE Id = @TipArticolId AND IsActive = 1)
    BEGIN
        RAISERROR('Tip articol invalid sau inactiv.', 16, 1);
        RETURN;
    END

    -- Check for duplicate ArticolCode
    IF EXISTS (SELECT 1 FROM dbo.Articole WHERE ArticolCode = @ArticolCode AND IsActive = 1)
    BEGIN
        RAISERROR('Cod articol deja existent.', 16, 1);
        RETURN;
    END

    DECLARE @Id UNIQUEIDENTIFIER = NEWID();
    INSERT INTO dbo.Articole (Id, ArticolCode, ArticolName, Description, TipArticolId,
                             UnitateMasura, PretAchizitie, PretVanzare, StocMinim, StocMaxim, IsStockable,
                             OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId,
                             CreatedAt, CreatedBy)
    VALUES (@Id, @ArticolCode, @ArticolName, @Description, @TipArticolId,
            @UnitateMasura, @PretAchizitie, @PretVanzare, @StocMinim, @StocMaxim, @IsStockable,
            @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId,
            GETDATE(), @CreatedBy);

    SELECT @Id AS Id;
END
GO

IF OBJECT_ID('dbo.sp_Articole_Update', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Articole_Update;
GO
CREATE PROCEDURE dbo.sp_Articole_Update
    @Id UNIQUEIDENTIFIER,
    @ArticolCode NVARCHAR(50),
    @ArticolName NVARCHAR(200),
    @Description NVARCHAR(500) = NULL,
    @TipArticolId UNIQUEIDENTIFIER,
    @UnitateMasura NVARCHAR(20) = 'BUC',
    @PretAchizitie DECIMAL(18,2) = NULL,
    @PretVanzare DECIMAL(18,2) = NULL,
    @StocMinim DECIMAL(18,2) = 0,
    @StocMaxim DECIMAL(18,2) = NULL,
    @IsStockable BIT = 1,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Validate TipArticolId exists and is active
    IF NOT EXISTS (SELECT 1 FROM dbo.ItemTypes WHERE Id = @TipArticolId AND IsActive = 1)
    BEGIN
        RAISERROR('Tip articol invalid sau inactiv.', 16, 1);
        RETURN;
    END

    -- Check for duplicate ArticolCode (excluding current record)
    IF EXISTS (SELECT 1 FROM dbo.Articole WHERE ArticolCode = @ArticolCode AND Id != @Id AND IsActive = 1)
    BEGIN
        RAISERROR('Cod articol deja existent.', 16, 1);
        RETURN;
    END

    UPDATE dbo.Articole
    SET ArticolCode = @ArticolCode,
        ArticolName = @ArticolName,
        Description = @Description,
        TipArticolId = @TipArticolId,
        UnitateMasura = @UnitateMasura,
        PretAchizitie = @PretAchizitie,
        PretVanzare = @PretVanzare,
        StocMinim = @StocMinim,
        StocMaxim = @StocMaxim,
        IsStockable = @IsStockable,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;

    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

IF OBJECT_ID('dbo.sp_Articole_Delete', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Articole_Delete;
GO
CREATE PROCEDURE dbo.sp_Articole_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Check if articol exists
    IF NOT EXISTS (SELECT 1 FROM dbo.Articole WHERE Id = @Id)
    BEGIN
        RAISERROR('Articol nu a fost găsit.', 16, 1);
        RETURN;
    END

    UPDATE dbo.Articole
    SET IsActive = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;

    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

IF OBJECT_ID('dbo.sp_Articole_Exists', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Articole_Exists;
GO
CREATE PROCEDURE dbo.sp_Articole_Exists
    @ArticolCode NVARCHAR(20),
    @ExcludeId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @ExcludeId IS NULL
    BEGIN
        SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Articole WHERE ArticolCode = @ArticolCode AND IsActive = 1) THEN 1 ELSE 0 END AS BIT) AS ExistsResult;
    END
    ELSE
    BEGIN
        SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Articole WHERE ArticolCode = @ArticolCode AND Id != @ExcludeId AND IsActive = 1) THEN 1 ELSE 0 END AS BIT) AS ExistsResult;
    END
END
GO

PRINT '✅ Stored procedures Articole create/ update/ delete/ get complete';