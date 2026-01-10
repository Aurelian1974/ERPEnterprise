-- =============================================
-- Stored Procedures for Persoane Feature
-- Fixes SQL Injection vulnerabilities
-- =============================================

-- =============================================
-- sp_Persoane_GetPaged - Secure paged query
-- =============================================
IF OBJECT_ID('dbo.sp_Persoane_GetPaged', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Persoane_GetPaged;
GO

CREATE PROCEDURE dbo.sp_Persoane_GetPaged
    @SearchTerm NVARCHAR(100) = NULL,
    @FilterField NVARCHAR(50) = NULL,
    @FilterOperator NVARCHAR(20) = NULL,
    @FilterValue NVARCHAR(500) = NULL,
    @SortField NVARCHAR(50) = 'CreatedAt',
    @SortDirection NVARCHAR(4) = 'DESC',
    @Skip INT = 0,
    @Take INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Whitelist validation for sort field
    IF @SortField NOT IN ('Nume', 'Prenume', 'CNP', 'Email', 'Oras', 'Judet', 'IsActive', 'CreatedAt')
        SET @SortField = 'CreatedAt';
    
    -- Whitelist validation for sort direction
    IF @SortDirection NOT IN ('ASC', 'DESC')
        SET @SortDirection = 'DESC';
    
    -- Whitelist validation for filter field
    IF @FilterField IS NOT NULL AND @FilterField NOT IN ('Nume', 'Prenume', 'CNP', 'Email', 'Oras', 'Judet', 'IsActive')
        SET @FilterField = NULL;
    
    DECLARE @SQL NVARCHAR(MAX);
    DECLARE @CountSQL NVARCHAR(MAX);
    DECLARE @WhereClause NVARCHAR(MAX) = N' WHERE 1=1 ';
    
    -- Search filter
    IF @SearchTerm IS NOT NULL
    BEGIN
        SET @WhereClause = @WhereClause + N' AND (Nume LIKE ''%'' + @SearchTerm + ''%'' 
                                                    OR Prenume LIKE ''%'' + @SearchTerm + ''%'' 
                                                    OR Email LIKE ''%'' + @SearchTerm + ''%'' 
                                                    OR CNP LIKE ''%'' + @SearchTerm + ''%'') ';
    END
    
    -- Column filter (parameterized)
    IF @FilterField IS NOT NULL AND @FilterValue IS NOT NULL
    BEGIN
        IF @FilterOperator = 'contains'
            SET @WhereClause = @WhereClause + N' AND ' + QUOTENAME(@FilterField) + N' LIKE ''%'' + @FilterValue + ''%'' ';
        ELSE IF @FilterOperator = 'equal'
            SET @WhereClause = @WhereClause + N' AND ' + QUOTENAME(@FilterField) + N' = @FilterValue ';
    END
    
    -- Build count query
    SET @CountSQL = N'SELECT COUNT(*) AS TotalCount FROM Persoane ' + @WhereClause;
    
    -- Build data query
    SET @SQL = N'SELECT * FROM Persoane ' + @WhereClause + 
               N' ORDER BY ' + QUOTENAME(@SortField) + N' ' + @SortDirection +
               N' OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY';
    
    -- Execute data query (returns first result set with data)
    EXEC sp_executesql @SQL, 
                       N'@SearchTerm NVARCHAR(100), @FilterValue NVARCHAR(500), @Skip INT, @Take INT', 
                       @SearchTerm, @FilterValue, @Skip, @Take;
    
    -- Execute count (returns second result set with count)
    EXEC sp_executesql @CountSQL, 
                       N'@SearchTerm NVARCHAR(100), @FilterValue NVARCHAR(500)', 
                       @SearchTerm, @FilterValue;
END
GO

-- =============================================
-- sp_Persoane_GetById
-- =============================================
IF OBJECT_ID('dbo.sp_Persoane_GetById', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Persoane_GetById;
GO

CREATE PROCEDURE dbo.sp_Persoane_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM Persoane WHERE Id = @Id;
END
GO

-- =============================================
-- sp_Persoane_GetByEmail
-- =============================================
IF OBJECT_ID('dbo.sp_Persoane_GetByEmail', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Persoane_GetByEmail;
GO

CREATE PROCEDURE dbo.sp_Persoane_GetByEmail
    @Email NVARCHAR(256)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM Persoane WHERE Email = @Email;
END
GO

-- =============================================
-- sp_Persoane_GetAllSimple
-- =============================================
IF OBJECT_ID('dbo.sp_Persoane_GetAllSimple', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Persoane_GetAllSimple;
GO

CREATE PROCEDURE dbo.sp_Persoane_GetAllSimple
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Add limit to prevent loading 10,000+ records
    SELECT TOP 1000 Id, Nume, Prenume, (Nume + ' ' + Prenume) AS NumeComplet
    FROM Persoane 
    WHERE IsActive = 1 
    ORDER BY Nume, Prenume;
END
GO

-- =============================================
-- sp_Persoane_Create
-- =============================================
IF OBJECT_ID('dbo.sp_Persoane_Create', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Persoane_Create;
GO

CREATE PROCEDURE dbo.sp_Persoane_Create
    @Id UNIQUEIDENTIFIER,
    @Nume NVARCHAR(100),
    @Prenume NVARCHAR(100),
    @CNP NVARCHAR(13) = NULL,
    @DataNasterii DATETIME2 = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @Adresa NVARCHAR(500) = NULL,
    @Oras NVARCHAR(100) = NULL,
    @Judet NVARCHAR(100) = NULL,
    @CodPostal NVARCHAR(10) = NULL,
    @Tara NVARCHAR(100) = 'Romania',
    @IsActive BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Generate new GUID if not provided
    IF @Id = '00000000-0000-0000-0000-000000000000'
        SET @Id = NEWID();
    
    INSERT INTO Persoane (Id, Nume, Prenume, CNP, DataNasterii, Email, Telefon, Adresa, Oras, Judet, CodPostal, Tara, IsActive, CreatedAt)
    VALUES (@Id, @Nume, @Prenume, @CNP, @DataNasterii, @Email, @Telefon, @Adresa, @Oras, @Judet, @CodPostal, @Tara, @IsActive, GETDATE());
    
    -- Return the created record
    SELECT * FROM Persoane WHERE Id = @Id;
END
GO

-- =============================================
-- sp_Persoane_Update
-- =============================================
IF OBJECT_ID('dbo.sp_Persoane_Update', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Persoane_Update;
GO

CREATE PROCEDURE dbo.sp_Persoane_Update
    @Id UNIQUEIDENTIFIER,
    @Nume NVARCHAR(100),
    @Prenume NVARCHAR(100),
    @CNP NVARCHAR(13) = NULL,
    @DataNasterii DATETIME2 = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @Adresa NVARCHAR(500) = NULL,
    @Oras NVARCHAR(100) = NULL,
    @Judet NVARCHAR(100) = NULL,
    @CodPostal NVARCHAR(10) = NULL,
    @Tara NVARCHAR(100) = NULL,
    @IsActive BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE Persoane 
    SET Nume = @Nume, 
        Prenume = @Prenume, 
        CNP = @CNP, 
        DataNasterii = @DataNasterii, 
        Email = @Email, 
        Telefon = @Telefon, 
        Adresa = @Adresa, 
        Oras = @Oras, 
        Judet = @Judet, 
        CodPostal = @CodPostal, 
        Tara = @Tara, 
        IsActive = @IsActive,
        UpdatedAt = GETDATE()
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- =============================================
-- sp_Persoane_Delete (Soft Delete)
-- =============================================
IF OBJECT_ID('dbo.sp_Persoane_Delete', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Persoane_Delete;
GO

CREATE PROCEDURE dbo.sp_Persoane_Delete
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Soft delete - set IsActive to 0
    UPDATE Persoane 
    SET IsActive = 0, 
        UpdatedAt = GETDATE()
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT '✅ Persoane stored procedures created successfully!';
