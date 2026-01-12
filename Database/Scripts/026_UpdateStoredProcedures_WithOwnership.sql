-- =============================================
-- ValyanERP - Updated Stored Procedures with Ownership
-- Script: 026_UpdateStoredProcedures_WithOwnership.sql
-- Data: 12 Ianuarie 2026
-- Descriere: Actualizează SP-urile pentru Persoane, Users, Partners
--            să includă coloanele OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 026_UpdateStoredProcedures_WithOwnership ===';
GO

-- =============================================
-- 1. PERSOANE: sp_Persoane_Create (cu ownership)
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
    @IsActive BIT = 1,
    -- Ownership columns
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Generate new GUID if not provided
    IF @Id = '00000000-0000-0000-0000-000000000000'
        SET @Id = NEWID();
    
    INSERT INTO Persoane (
        Id, Nume, Prenume, CNP, DataNasterii, Email, Telefon, 
        Adresa, Oras, Judet, CodPostal, Tara, IsActive, CreatedAt,
        OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId, CreatedBy
    )
    VALUES (
        @Id, @Nume, @Prenume, @CNP, @DataNasterii, @Email, @Telefon, 
        @Adresa, @Oras, @Judet, @CodPostal, @Tara, @IsActive, GETDATE(),
        @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId, @CreatedBy
    );
    
    -- Return the created record
    SELECT * FROM Persoane WHERE Id = @Id;
END
GO

PRINT '✅ sp_Persoane_Create actualizat cu ownership';
GO

-- =============================================
-- 2. PERSOANE: sp_Persoane_Update (cu ownership)
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
    @IsActive BIT = 1,
    -- Ownership columns (optional - only update if provided)
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
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
        -- Only update ownership if explicitly provided
        OwnerCompanyId = COALESCE(@OwnerCompanyId, OwnerCompanyId),
        OwnerWorkPlaceId = COALESCE(@OwnerWorkPlaceId, OwnerWorkPlaceId),
        OwnerLocationId = COALESCE(@OwnerLocationId, OwnerLocationId),
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT '✅ sp_Persoane_Update actualizat cu ownership';
GO

-- =============================================
-- 3. PERSOANE: sp_Persoane_GetPaged (cu filtrare pe ownership)
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
    @Take INT = 20,
    -- Security: Filter by user's visible perimeter
    @CurrentUserId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Whitelist validation for sort field
    IF @SortField NOT IN ('Nume', 'Prenume', 'CNP', 'Email', 'Oras', 'Judet', 'IsActive', 'CreatedAt', 'OwnerCompanyId')
        SET @SortField = 'CreatedAt';
    
    -- Whitelist validation for sort direction
    IF @SortDirection NOT IN ('ASC', 'DESC')
        SET @SortDirection = 'DESC';
    
    -- Whitelist validation for filter field
    IF @FilterField IS NOT NULL AND @FilterField NOT IN ('Nume', 'Prenume', 'CNP', 'Email', 'Oras', 'Judet', 'IsActive', 'OwnerCompanyId')
        SET @FilterField = NULL;
    
    -- Check if user is Admin (bypass filter)
    DECLARE @IsAdmin BIT = 0;
    IF @CurrentUserId IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1 FROM UserRoles ur
            INNER JOIN Roles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @CurrentUserId AND r.NormalizedName = 'ADMIN'
        )
            SET @IsAdmin = 1;
    END
    
    DECLARE @SQL NVARCHAR(MAX);
    DECLARE @CountSQL NVARCHAR(MAX);
    DECLARE @WhereClause NVARCHAR(MAX) = N' WHERE 1=1 ';
    
    -- Search filter
    IF @SearchTerm IS NOT NULL
    BEGIN
        SET @WhereClause = @WhereClause + N' AND (p.Nume LIKE ''%'' + @SearchTerm + ''%'' 
                                                    OR p.Prenume LIKE ''%'' + @SearchTerm + ''%'' 
                                                    OR p.Email LIKE ''%'' + @SearchTerm + ''%'' 
                                                    OR p.CNP LIKE ''%'' + @SearchTerm + ''%'') ';
    END
    
    -- Column filter (parameterized)
    IF @FilterField IS NOT NULL AND @FilterValue IS NOT NULL
    BEGIN
        IF @FilterOperator = 'contains'
            SET @WhereClause = @WhereClause + N' AND p.' + QUOTENAME(@FilterField) + N' LIKE ''%'' + @FilterValue + ''%'' ';
        ELSE IF @FilterOperator = 'equal'
            SET @WhereClause = @WhereClause + N' AND p.' + QUOTENAME(@FilterField) + N' = @FilterValue ';
    END
    
    -- Security filter: Only show data user can see (unless Admin)
    IF @CurrentUserId IS NOT NULL AND @IsAdmin = 0
    BEGIN
        SET @WhereClause = @WhereClause + N' AND (
            p.OwnerCompanyId IN (SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(@CurrentUserId))
            OR p.CreatedBy = @CurrentUserId
            OR p.OwnerCompanyId IS NULL
        ) ';
    END
    
    -- Build count query
    SET @CountSQL = N'SELECT COUNT(*) AS TotalCount FROM Persoane p ' + @WhereClause;
    
    -- Build data query with ownership info
    SET @SQL = N'SELECT p.*, 
                        oc.Denumire AS OwnerCompanyName 
                 FROM Persoane p 
                 LEFT JOIN Companies oc ON oc.Id = p.OwnerCompanyId ' 
               + @WhereClause + 
               N' ORDER BY p.' + QUOTENAME(@SortField) + N' ' + @SortDirection +
               N' OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY';
    
    -- Execute data query
    EXEC sp_executesql @SQL, 
                       N'@SearchTerm NVARCHAR(100), @FilterValue NVARCHAR(500), @Skip INT, @Take INT, @CurrentUserId UNIQUEIDENTIFIER', 
                       @SearchTerm, @FilterValue, @Skip, @Take, @CurrentUserId;
    
    -- Execute count
    EXEC sp_executesql @CountSQL, 
                       N'@SearchTerm NVARCHAR(100), @FilterValue NVARCHAR(500), @CurrentUserId UNIQUEIDENTIFIER', 
                       @SearchTerm, @FilterValue, @CurrentUserId;
END
GO

PRINT '✅ sp_Persoane_GetPaged actualizat cu filtrare pe ownership';
GO

-- =============================================
-- 4. USERS: sp_Users_Create (cu ownership)
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
    @IsActive BIT = 1,
    -- Ownership columns
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Generate new GUID if not provided
    IF @Id = '00000000-0000-0000-0000-000000000000'
        SET @Id = NEWID();
    
    INSERT INTO Users (
        Id, PersoanaId, UserName, NormalizedUserName, Email, NormalizedEmail, 
        PasswordHash, IsActive, CreatedAt, SecurityStamp,
        OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
    )
    VALUES (
        @Id, @PersoanaId, @UserName, @NormalizedUserName, @Email, @NormalizedEmail,
        @PasswordHash, @IsActive, GETDATE(), NEWID(),
        @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId
    );
    
    -- Return the created record
    SELECT * FROM Users WHERE Id = @Id;
END
GO

PRINT '✅ sp_Users_Create actualizat cu ownership';
GO

-- =============================================
-- 5. USERS: sp_Users_Update (cu ownership)
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
    @IsActive BIT,
    -- Ownership columns
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL
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
        OwnerCompanyId = COALESCE(@OwnerCompanyId, OwnerCompanyId),
        OwnerWorkPlaceId = COALESCE(@OwnerWorkPlaceId, OwnerWorkPlaceId),
        OwnerLocationId = COALESCE(@OwnerLocationId, OwnerLocationId),
        UpdatedAt = GETDATE()
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT '✅ sp_Users_Update actualizat cu ownership';
GO

-- =============================================
-- 6. USERS: sp_Users_GetPaged (cu filtrare pe ownership)
-- =============================================
IF OBJECT_ID('dbo.sp_Users_GetPaged', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Users_GetPaged;
GO

CREATE PROCEDURE dbo.sp_Users_GetPaged
    @SearchTerm NVARCHAR(100) = NULL,
    @SortField NVARCHAR(50) = 'CreatedAt',
    @SortDirection NVARCHAR(4) = 'DESC',
    @Skip INT = 0,
    @Take INT = 20,
    -- Security: Filter by user's visible perimeter
    @CurrentUserId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Whitelist validation for sort field
    IF @SortField NOT IN ('UserName', 'Email', 'NumeComplet', 'IsActive', 'CreatedAt', 'OwnerCompanyId')
        SET @SortField = 'CreatedAt';
    
    -- Whitelist validation for sort direction
    IF @SortDirection NOT IN ('ASC', 'DESC')
        SET @SortDirection = 'DESC';
    
    -- Check if user is Admin (bypass filter)
    DECLARE @IsAdmin BIT = 0;
    IF @CurrentUserId IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1 FROM UserRoles ur
            INNER JOIN Roles r ON r.Id = ur.RoleId
            WHERE ur.UserId = @CurrentUserId AND r.NormalizedName = 'ADMIN'
        )
            SET @IsAdmin = 1;
    END
    
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
    
    -- Security filter: Only show data user can see (unless Admin)
    IF @CurrentUserId IS NOT NULL AND @IsAdmin = 0
    BEGIN
        SET @WhereClause = @WhereClause + N' AND (
            u.OwnerCompanyId IN (SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(@CurrentUserId))
            OR u.Id = @CurrentUserId
            OR u.OwnerCompanyId IS NULL
        ) ';
    END
    
    -- Build count query
    SET @CountSQL = N'SELECT COUNT(*) AS TotalCount 
                     FROM Users u
                     INNER JOIN Persoane p ON u.PersoanaId = p.Id ' + @WhereClause;
    
    -- Build data query with ownership
    SET @SQL = N'SELECT u.Id, u.PersoanaId, u.UserName, u.NormalizedUserName, 
                        u.Email, u.NormalizedEmail, u.EmailConfirmed, 
                        u.PasswordHash, u.SecurityStamp, u.ConcurrencyStamp,
                        u.FirstName, u.LastName, u.PhoneNumber, u.PhoneNumberConfirmed,
                        u.TwoFactorEnabled, u.LockoutEnd, u.LockoutEnabled, u.AccessFailedCount,
                        u.IsActive, u.CreatedAt, u.UpdatedAt,
                        u.OwnerCompanyId, u.OwnerWorkPlaceId, u.OwnerLocationId,
                        (p.Nume + '' '' + p.Prenume) AS NumeComplet,
                        oc.Denumire AS OwnerCompanyName
                 FROM Users u
                 INNER JOIN Persoane p ON u.PersoanaId = p.Id
                 LEFT JOIN Companies oc ON oc.Id = u.OwnerCompanyId ' + @WhereClause +
               N' ORDER BY ';
    
    -- Handle sort field mapping
    IF @SortField = 'NumeComplet'
        SET @SQL = @SQL + N'(p.Nume + '' '' + p.Prenume) ' + @SortDirection;
    ELSE IF @SortField = 'CreatedAt'
        SET @SQL = @SQL + N'u.CreatedAt ' + @SortDirection;
    ELSE
        SET @SQL = @SQL + N'u.' + QUOTENAME(@SortField) + N' ' + @SortDirection;
    
    SET @SQL = @SQL + N' OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY';
    
    -- Execute data query
    EXEC sp_executesql @SQL, 
                       N'@SearchTerm NVARCHAR(100), @Skip INT, @Take INT, @CurrentUserId UNIQUEIDENTIFIER', 
                       @SearchTerm, @Skip, @Take, @CurrentUserId;
    
    -- Execute count
    EXEC sp_executesql @CountSQL, 
                       N'@SearchTerm NVARCHAR(100), @CurrentUserId UNIQUEIDENTIFIER', 
                       @SearchTerm, @CurrentUserId;
END
GO

PRINT '✅ sp_Users_GetPaged actualizat cu filtrare pe ownership';
GO

-- =============================================
-- 7. PARTNERS: sp_Partners_Create (dacă există)
-- =============================================
IF OBJECT_ID('dbo.sp_Partners_Create', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Partners_Create;
GO

CREATE PROCEDURE dbo.sp_Partners_Create
    @Id UNIQUEIDENTIFIER OUTPUT,
    @Cod NVARCHAR(20),
    @Categoria TINYINT,
    @TipEntitate NVARCHAR(20),
    @RolPartener INT = 0,
    @Denumire NVARCHAR(200) = NULL,
    @DenumireScurta NVARCHAR(50) = NULL,
    @Nume NVARCHAR(100) = NULL,
    @Prenume NVARCHAR(100) = NULL,
    @CNP NVARCHAR(13) = NULL,
    @CUI NVARCHAR(20) = NULL,
    @CIF NVARCHAR(20) = NULL,
    @RegCom NVARCHAR(50) = NULL,
    @TaraOrigine NVARCHAR(3) = 'RO',
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @EstePlatitorTVA BIT = 0,
    @PartnerStatus VARCHAR(20) = 'Activ',
    @Observatii NVARCHAR(2000) = NULL,
    -- Ownership columns
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Generate new GUID
    SET @Id = NEWID();
    
    -- Auto-generate Cod if not provided
    IF @Cod IS NULL OR @Cod = ''
    BEGIN
        DECLARE @NextNum INT;
        SELECT @NextNum = ISNULL(MAX(CAST(SUBSTRING(Cod, 3, 10) AS INT)), 0) + 1
        FROM Partners WHERE Cod LIKE 'P-%';
        SET @Cod = 'P-' + RIGHT('000000' + CAST(@NextNum AS VARCHAR), 6);
    END
    
    INSERT INTO Partners (
        Id, Cod, Categoria, TipEntitate, RolPartener,
        Denumire, DenumireScurta, Nume, Prenume,
        CNP, CUI, CIF, RegCom, TaraOrigine,
        Email, Telefon, [EstePlatitorTVA], PartnerStatus,
        Observatii, IsActive, EsteActiv, CreatedAt, CreatedBy,
        OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId
    )
    VALUES (
        @Id, @Cod, @Categoria, @TipEntitate, @RolPartener,
        @Denumire, @DenumireScurta, @Nume, @Prenume,
        @CNP, @CUI, @CIF, @RegCom, @TaraOrigine,
        @Email, @Telefon, @EstePlatitorTVA, @PartnerStatus,
        @Observatii, 1, 1, GETDATE(), @CreatedBy,
        @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId
    );
    
    SELECT @Id AS NewId, @Cod AS NewCod;
END
GO

PRINT '✅ sp_Partners_Create creat cu ownership';
GO

-- =============================================
-- 8. PARTNERS: sp_Partners_Update (cu ownership)
-- =============================================
IF OBJECT_ID('dbo.sp_Partners_Update', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Partners_Update;
GO

CREATE PROCEDURE dbo.sp_Partners_Update
    @Id UNIQUEIDENTIFIER,
    @Categoria TINYINT = NULL,
    @TipEntitate NVARCHAR(20) = NULL,
    @RolPartener INT = NULL,
    @Denumire NVARCHAR(200) = NULL,
    @DenumireScurta NVARCHAR(50) = NULL,
    @Nume NVARCHAR(100) = NULL,
    @Prenume NVARCHAR(100) = NULL,
    @CNP NVARCHAR(13) = NULL,
    @CUI NVARCHAR(20) = NULL,
    @CIF NVARCHAR(20) = NULL,
    @RegCom NVARCHAR(50) = NULL,
    @TaraOrigine NVARCHAR(3) = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @EstePlatitorTVA BIT = NULL,
    @PartnerStatus VARCHAR(20) = NULL,
    @Observatii NVARCHAR(2000) = NULL,
    @IsActive BIT = NULL,
    -- Ownership columns
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE Partners SET
        Categoria = COALESCE(@Categoria, Categoria),
        TipEntitate = COALESCE(@TipEntitate, TipEntitate),
        RolPartener = COALESCE(@RolPartener, RolPartener),
        Denumire = COALESCE(@Denumire, Denumire),
        DenumireScurta = COALESCE(@DenumireScurta, DenumireScurta),
        Nume = COALESCE(@Nume, Nume),
        Prenume = COALESCE(@Prenume, Prenume),
        CNP = COALESCE(@CNP, CNP),
        CUI = COALESCE(@CUI, CUI),
        CIF = COALESCE(@CIF, CIF),
        RegCom = COALESCE(@RegCom, RegCom),
        TaraOrigine = COALESCE(@TaraOrigine, TaraOrigine),
        Email = COALESCE(@Email, Email),
        Telefon = COALESCE(@Telefon, Telefon),
        [EstePlatitorTVA] = COALESCE(@EstePlatitorTVA, [EstePlatitorTVA]),
        PartnerStatus = COALESCE(@PartnerStatus, PartnerStatus),
        Observatii = COALESCE(@Observatii, Observatii),
        IsActive = COALESCE(@IsActive, IsActive),
        EsteActiv = COALESCE(@IsActive, EsteActiv),
        OwnerCompanyId = COALESCE(@OwnerCompanyId, OwnerCompanyId),
        OwnerWorkPlaceId = COALESCE(@OwnerWorkPlaceId, OwnerWorkPlaceId),
        OwnerLocationId = COALESCE(@OwnerLocationId, OwnerLocationId),
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT '✅ sp_Partners_Update creat cu ownership';
GO

PRINT '=== Migrare 026_UpdateStoredProcedures_WithOwnership completă ===';
GO
