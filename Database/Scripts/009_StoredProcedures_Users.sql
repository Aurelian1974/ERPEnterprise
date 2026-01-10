-- =============================================
-- Stored Procedures for Users Feature
-- Fixes SQL Injection vulnerabilities
-- =============================================

-- =============================================
-- sp_Users_GetPaged - Secure paged query with Persoane JOIN
-- =============================================
IF OBJECT_ID('dbo.sp_Users_GetPaged', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Users_GetPaged;
GO

CREATE PROCEDURE dbo.sp_Users_GetPaged
    @SearchTerm NVARCHAR(100) = NULL,
    @SortField NVARCHAR(50) = 'CreatedAt',
    @SortDirection NVARCHAR(4) = 'DESC',
    @Skip INT = 0,
    @Take INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Whitelist validation for sort field
    IF @SortField NOT IN ('UserName', 'Email', 'NumeComplet', 'IsActive', 'CreatedAt')
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
        SET @WhereClause = @WhereClause + N' AND (u.UserName LIKE ''%'' + @SearchTerm + ''%'' 
                                                    OR u.Email LIKE ''%'' + @SearchTerm + ''%'' 
                                                    OR (p.Nume + '' '' + p.Prenume) LIKE ''%'' + @SearchTerm + ''%'') ';
    END
    
    -- Build count query
    SET @CountSQL = N'SELECT COUNT(*) AS TotalCount 
                     FROM Users u
                     INNER JOIN Persoane p ON u.PersoanaId = p.Id ' + @WhereClause;
    
    -- Build data query - explicitly list all Users columns to avoid ambiguity with JOIN
    SET @SQL = N'SELECT u.Id, u.PersoanaId, u.UserName, u.NormalizedUserName, 
                        u.Email, u.NormalizedEmail, u.EmailConfirmed, 
                        u.PasswordHash, u.SecurityStamp, u.ConcurrencyStamp,
                        u.FirstName, u.LastName, u.PhoneNumber, u.PhoneNumberConfirmed,
                        u.TwoFactorEnabled, u.LockoutEnd, u.LockoutEnabled, u.AccessFailedCount,
                        u.IsActive, u.CreatedAt, u.UpdatedAt,
                        (p.Nume + '' '' + p.Prenume) AS NumeComplet
                 FROM Users u
                 INNER JOIN Persoane p ON u.PersoanaId = p.Id ' + @WhereClause +
               N' ORDER BY ';
    
    -- Handle sort field mapping
    IF @SortField = 'NumeComplet'
        SET @SQL = @SQL + N'(p.Nume + '' '' + p.Prenume) ' + @SortDirection;
    ELSE IF @SortField = 'CreatedAt'
        SET @SQL = @SQL + N'u.CreatedAt ' + @SortDirection;
    ELSE
        SET @SQL = @SQL + N'u.' + QUOTENAME(@SortField) + N' ' + @SortDirection;
    
    SET @SQL = @SQL + N' OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY';
    
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

-- =============================================
-- sp_Users_Create
-- =============================================
IF OBJECT_ID('dbo.sp_Users_Create', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Users_Create;
GO

CREATE PROCEDURE dbo.sp_Users_Create
    @Id UNIQUEIDENTIFIER,
    @PersoanaId UNIQUEIDENTIFIER,
    @UserName NVARCHAR(256),
    @NormalizedUserName NVARCHAR(256),
    @Email NVARCHAR(256),
    @NormalizedEmail NVARCHAR(256),
    @PasswordHash NVARCHAR(MAX),
    @IsActive BIT = 1
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Generate new GUID if not provided
    IF @Id = '00000000-0000-0000-0000-000000000000'
        SET @Id = NEWID();
    
    INSERT INTO Users (
        Id, PersoanaId, UserName, NormalizedUserName, Email, NormalizedEmail, 
        PasswordHash, IsActive, CreatedAt, SecurityStamp
    )
    VALUES (
        @Id, @PersoanaId, @UserName, @NormalizedUserName, @Email, @NormalizedEmail,
        @PasswordHash, @IsActive, GETDATE(), NEWID()
    );
    
    -- Return the created record
    SELECT * FROM Users WHERE Id = @Id;
END
GO

-- =============================================
-- sp_Users_Update
-- =============================================
IF OBJECT_ID('dbo.sp_Users_Update', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Users_Update;
GO

CREATE PROCEDURE dbo.sp_Users_Update
    @Id UNIQUEIDENTIFIER,
    @PersoanaId UNIQUEIDENTIFIER,
    @UserName NVARCHAR(256),
    @NormalizedUserName NVARCHAR(256),
    @Email NVARCHAR(256),
    @NormalizedEmail NVARCHAR(256),
    @IsActive BIT
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE Users
    SET PersoanaId = @PersoanaId,
        UserName = @UserName,
        NormalizedUserName = @NormalizedUserName,
        Email = @Email,
        NormalizedEmail = @NormalizedEmail,
        IsActive = @IsActive,
        UpdatedAt = GETDATE()
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- =============================================
-- sp_Users_Delete (Soft Delete)
-- =============================================
IF OBJECT_ID('dbo.sp_Users_Delete', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Users_Delete;
GO

CREATE PROCEDURE dbo.sp_Users_Delete
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Soft delete - set IsActive to 0
    UPDATE Users 
    SET IsActive = 0, 
        UpdatedAt = GETDATE()
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

-- =============================================
-- sp_Users_GetById
-- =============================================
IF OBJECT_ID('dbo.sp_Users_GetById', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Users_GetById;
GO

CREATE PROCEDURE dbo.sp_Users_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT u.Id, u.UserName, u.Email, u.PhoneNumber, u.IsActive, u.CreatedAt, u.PersoanaId,
           (p.Nume + ' ' + p.Prenume) AS NumeComplet
    FROM Users u
    INNER JOIN Persoane p ON u.PersoanaId = p.Id
    WHERE u.Id = @Id;
END
GO

PRINT '✅ Users stored procedures created successfully!';
