-- =============================================
-- ValyanERP - Societatea Proprie Stored Procedures
-- Script: 018_StoredProcedures_SocietateaProprie.sql
-- Data: 11 Ianuarie 2026
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 018_StoredProcedures_SocietateaProprie ===';
GO

-- =============================================
-- COMPANY GROUPS - Stored Procedures
-- =============================================

-- Get All Company Groups
IF OBJECT_ID('dbo.sp_CompanyGroups_GetAll', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_CompanyGroups_GetAll;
GO

CREATE PROCEDURE dbo.sp_CompanyGroups_GetAll
    @IncludeInactive BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        Id,
        Denumire,
        Descriere,
        LogoUrl,
        Website,
        IsActive,
        SortOrder,
        CreatedAt,
        CreatedBy,
        UpdatedAt,
        UpdatedBy,
        (SELECT COUNT(*) FROM Companies c WHERE c.GroupId = CompanyGroups.Id AND c.IsActive = 1) AS CompanyCount
    FROM CompanyGroups
    WHERE IsActive = 1 OR @IncludeInactive = 1
    ORDER BY SortOrder, Denumire;
END;
GO

PRINT '✅ sp_CompanyGroups_GetAll creat';
GO

-- Get Company Group By Id
IF OBJECT_ID('dbo.sp_CompanyGroups_GetById', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_CompanyGroups_GetById;
GO

CREATE PROCEDURE dbo.sp_CompanyGroups_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        Id,
        Denumire,
        Descriere,
        LogoUrl,
        Website,
        IsActive,
        SortOrder,
        CreatedAt,
        CreatedBy,
        UpdatedAt,
        UpdatedBy,
        (SELECT COUNT(*) FROM Companies c WHERE c.GroupId = CompanyGroups.Id AND c.IsActive = 1) AS CompanyCount
    FROM CompanyGroups
    WHERE Id = @Id;
END;
GO

PRINT '✅ sp_CompanyGroups_GetById creat';
GO

-- Create Company Group
IF OBJECT_ID('dbo.sp_CompanyGroups_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_CompanyGroups_Create;
GO

CREATE PROCEDURE dbo.sp_CompanyGroups_Create
    @Denumire NVARCHAR(200),
    @Descriere NVARCHAR(500) = NULL,
    @LogoUrl NVARCHAR(500) = NULL,
    @Website NVARCHAR(200) = NULL,
    @SortOrder INT = 0,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    
    INSERT INTO CompanyGroups (Id, Denumire, Descriere, LogoUrl, Website, SortOrder, CreatedBy)
    VALUES (@NewId, @Denumire, @Descriere, @LogoUrl, @Website, @SortOrder, @CreatedBy);
    
    SELECT @NewId AS Id;
END;
GO

PRINT '✅ sp_CompanyGroups_Create creat';
GO

-- Update Company Group
IF OBJECT_ID('dbo.sp_CompanyGroups_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_CompanyGroups_Update;
GO

CREATE PROCEDURE dbo.sp_CompanyGroups_Update
    @Id UNIQUEIDENTIFIER,
    @Denumire NVARCHAR(200),
    @Descriere NVARCHAR(500) = NULL,
    @LogoUrl NVARCHAR(500) = NULL,
    @Website NVARCHAR(200) = NULL,
    @SortOrder INT = 0,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE CompanyGroups
    SET 
        Denumire = @Denumire,
        Descriere = @Descriere,
        LogoUrl = @LogoUrl,
        Website = @Website,
        SortOrder = @SortOrder,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_CompanyGroups_Update creat';
GO

-- Delete (Soft) Company Group
IF OBJECT_ID('dbo.sp_CompanyGroups_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_CompanyGroups_Delete;
GO

CREATE PROCEDURE dbo.sp_CompanyGroups_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Check if group has active companies
    IF EXISTS (SELECT 1 FROM Companies WHERE GroupId = @Id AND IsActive = 1)
    BEGIN
        RAISERROR('Nu se poate șterge grupul. Există companii active asociate.', 16, 1);
        RETURN;
    END
    
    UPDATE CompanyGroups
    SET 
        IsActive = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_CompanyGroups_Delete creat';
GO

-- =============================================
-- COMPANIES - Stored Procedures
-- =============================================

-- Get All Companies (with view data)
IF OBJECT_ID('dbo.sp_Companies_GetAll', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Companies_GetAll;
GO

CREATE PROCEDURE dbo.sp_Companies_GetAll
    @IncludeInactive BIT = 0,
    @GroupId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT *
    FROM vw_CompanyWithSediuSocial
    WHERE (IsActive = 1 OR @IncludeInactive = 1)
      AND (@GroupId IS NULL OR GroupId = @GroupId)
    ORDER BY SortOrder, Denumire;
END;
GO

PRINT '✅ sp_Companies_GetAll creat';
GO

-- Get Company By Id
IF OBJECT_ID('dbo.sp_Companies_GetById', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Companies_GetById;
GO

CREATE PROCEDURE dbo.sp_Companies_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM vw_CompanyWithSediuSocial WHERE Id = @Id;
END;
GO

PRINT '✅ sp_Companies_GetById creat';
GO

-- Get Company By CUI
IF OBJECT_ID('dbo.sp_Companies_GetByCUI', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Companies_GetByCUI;
GO

CREATE PROCEDURE dbo.sp_Companies_GetByCUI
    @CUI NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM vw_CompanyWithSediuSocial WHERE CUI = @CUI;
END;
GO

PRINT '✅ sp_Companies_GetByCUI creat';
GO

-- Create Company
IF OBJECT_ID('dbo.sp_Companies_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Companies_Create;
GO

CREATE PROCEDURE dbo.sp_Companies_Create
    @CUI NVARCHAR(20),
    @Denumire NVARCHAR(200),
    @DenumireScurta NVARCHAR(50) = NULL,
    @RegCom NVARCHAR(50) = NULL,
    @GroupId UNIQUEIDENTIFIER = NULL,
    @ParentCompanyId UNIQUEIDENTIFIER = NULL,
    @TipCompanie TINYINT = 0,
    @ProcentDetinere DECIMAL(5,2) = NULL,
    @CapitalSocial DECIMAL(18,2) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @Email NVARCHAR(100) = NULL,
    @Website NVARCHAR(200) = NULL,
    @LogoUrl NVARCHAR(500) = NULL,
    @IsPrincipal BIT = 0,
    @SortOrder INT = 0,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Check CUI uniqueness
    IF EXISTS (SELECT 1 FROM Companies WHERE CUI = @CUI)
    BEGIN
        RAISERROR('CUI-ul %s există deja în sistem.', 16, 1, @CUI);
        RETURN;
    END
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    
    INSERT INTO Companies (
        Id, CUI, Denumire, DenumireScurta, RegCom, GroupId, ParentCompanyId,
        TipCompanie, ProcentDetinere, CapitalSocial, Telefon, Email, Website, LogoUrl,
        IsPrincipal, SortOrder, CreatedBy
    )
    VALUES (
        @NewId, @CUI, @Denumire, @DenumireScurta, @RegCom, @GroupId, @ParentCompanyId,
        @TipCompanie, @ProcentDetinere, @CapitalSocial, @Telefon, @Email, @Website, @LogoUrl,
        @IsPrincipal, @SortOrder, @CreatedBy
    );
    
    SELECT @NewId AS Id;
END;
GO

PRINT '✅ sp_Companies_Create creat';
GO

-- Update Company
IF OBJECT_ID('dbo.sp_Companies_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Companies_Update;
GO

CREATE PROCEDURE dbo.sp_Companies_Update
    @Id UNIQUEIDENTIFIER,
    @CUI NVARCHAR(20),
    @Denumire NVARCHAR(200),
    @DenumireScurta NVARCHAR(50) = NULL,
    @RegCom NVARCHAR(50) = NULL,
    @GroupId UNIQUEIDENTIFIER = NULL,
    @ParentCompanyId UNIQUEIDENTIFIER = NULL,
    @TipCompanie TINYINT = 0,
    @ProcentDetinere DECIMAL(5,2) = NULL,
    @CapitalSocial DECIMAL(18,2) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @Email NVARCHAR(100) = NULL,
    @Website NVARCHAR(200) = NULL,
    @LogoUrl NVARCHAR(500) = NULL,
    @IsPrincipal BIT = 0,
    @SortOrder INT = 0,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Check CUI uniqueness (exclude current record)
    IF EXISTS (SELECT 1 FROM Companies WHERE CUI = @CUI AND Id <> @Id)
    BEGIN
        RAISERROR('CUI-ul %s există deja în sistem.', 16, 1, @CUI);
        RETURN;
    END
    
    UPDATE Companies
    SET 
        CUI = @CUI,
        Denumire = @Denumire,
        DenumireScurta = @DenumireScurta,
        RegCom = @RegCom,
        GroupId = @GroupId,
        ParentCompanyId = @ParentCompanyId,
        TipCompanie = @TipCompanie,
        ProcentDetinere = @ProcentDetinere,
        CapitalSocial = @CapitalSocial,
        Telefon = @Telefon,
        Email = @Email,
        Website = @Website,
        LogoUrl = @LogoUrl,
        IsPrincipal = @IsPrincipal,
        SortOrder = @SortOrder,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_Companies_Update creat';
GO

-- Delete (Soft) Company
IF OBJECT_ID('dbo.sp_Companies_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Companies_Delete;
GO

CREATE PROCEDURE dbo.sp_Companies_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Check if company has active workplaces
    IF EXISTS (SELECT 1 FROM WorkPlaces WHERE CompanyId = @Id AND IsActive = 1)
    BEGIN
        RAISERROR('Nu se poate șterge compania. Există puncte de lucru active asociate.', 16, 1);
        RETURN;
    END
    
    -- Check if company has active locations
    IF EXISTS (SELECT 1 FROM Locations WHERE CompanyId = @Id AND IsActive = 1)
    BEGIN
        RAISERROR('Nu se poate șterge compania. Există locații active asociate.', 16, 1);
        RETURN;
    END
    
    -- Check if company is parent for other companies
    IF EXISTS (SELECT 1 FROM Companies WHERE ParentCompanyId = @Id AND IsActive = 1)
    BEGIN
        RAISERROR('Nu se poate șterge compania. Este companie părinte pentru alte companii.', 16, 1);
        RETURN;
    END
    
    UPDATE Companies
    SET 
        IsActive = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_Companies_Delete creat';
GO

-- =============================================
-- WORKPLACES - Stored Procedures
-- =============================================

-- Get All WorkPlaces
IF OBJECT_ID('dbo.sp_WorkPlaces_GetAll', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_WorkPlaces_GetAll;
GO

CREATE PROCEDURE dbo.sp_WorkPlaces_GetAll
    @IncludeInactive BIT = 0,
    @CompanyId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        wp.*,
        c.Denumire AS CompanyName,
        c.CUI AS CompanyCUI,
        (SELECT COUNT(*) FROM Locations l WHERE l.WorkPlaceId = wp.Id AND l.IsActive = 1) AS LocationCount
    FROM WorkPlaces wp
    INNER JOIN Companies c ON c.Id = wp.CompanyId
    WHERE (wp.IsActive = 1 OR @IncludeInactive = 1)
      AND (@CompanyId IS NULL OR wp.CompanyId = @CompanyId)
    ORDER BY wp.SortOrder, wp.Denumire;
END;
GO

PRINT '✅ sp_WorkPlaces_GetAll creat';
GO

-- Get WorkPlace By Id
IF OBJECT_ID('dbo.sp_WorkPlaces_GetById', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_WorkPlaces_GetById;
GO

CREATE PROCEDURE dbo.sp_WorkPlaces_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        wp.*,
        c.Denumire AS CompanyName,
        c.CUI AS CompanyCUI,
        (SELECT COUNT(*) FROM Locations l WHERE l.WorkPlaceId = wp.Id AND l.IsActive = 1) AS LocationCount
    FROM WorkPlaces wp
    INNER JOIN Companies c ON c.Id = wp.CompanyId
    WHERE wp.Id = @Id;
END;
GO

PRINT '✅ sp_WorkPlaces_GetById creat';
GO

-- Create WorkPlace
IF OBJECT_ID('dbo.sp_WorkPlaces_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_WorkPlaces_Create;
GO

CREATE PROCEDURE dbo.sp_WorkPlaces_Create
    @CompanyId UNIQUEIDENTIFIER,
    @Denumire NVARCHAR(200),
    @CodONRC NVARCHAR(50) = NULL,
    @TipPunctLucru TINYINT = 0,
    @RegimJuridic TINYINT = 0,
    @Adresa NVARCHAR(500),
    @Localitate NVARCHAR(100),
    @Judet NVARCHAR(50),
    @CodPostal NVARCHAR(10) = NULL,
    @Tara NVARCHAR(50) = N'România',
    @Telefon NVARCHAR(20) = NULL,
    @Email NVARCHAR(100) = NULL,
    @EsteSediuSocial BIT = 0,
    @DataInregistrare DATE = NULL,
    @SortOrder INT = 0,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Check if company exists
    IF NOT EXISTS (SELECT 1 FROM Companies WHERE Id = @CompanyId AND IsActive = 1)
    BEGIN
        RAISERROR('Compania specificată nu există sau este inactivă.', 16, 1);
        RETURN;
    END
    
    -- If EsteSediuSocial = 1, check that no other active sediu social exists for this company
    IF @EsteSediuSocial = 1 AND EXISTS (
        SELECT 1 FROM WorkPlaces WHERE CompanyId = @CompanyId AND EsteSediuSocial = 1 AND IsActive = 1
    )
    BEGIN
        RAISERROR('Compania are deja un sediu social activ. Dezactivați-l înainte de a crea altul.', 16, 1);
        RETURN;
    END
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    
    INSERT INTO WorkPlaces (
        Id, CompanyId, Denumire, CodONRC, TipPunctLucru, RegimJuridic,
        Adresa, Localitate, Judet, CodPostal, Tara, Telefon, Email,
        EsteSediuSocial, DataInregistrare, SortOrder, CreatedBy
    )
    VALUES (
        @NewId, @CompanyId, @Denumire, @CodONRC, @TipPunctLucru, @RegimJuridic,
        @Adresa, @Localitate, @Judet, @CodPostal, @Tara, @Telefon, @Email,
        @EsteSediuSocial, @DataInregistrare, @SortOrder, @CreatedBy
    );
    
    SELECT @NewId AS Id;
END;
GO

PRINT '✅ sp_WorkPlaces_Create creat';
GO

-- Update WorkPlace
IF OBJECT_ID('dbo.sp_WorkPlaces_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_WorkPlaces_Update;
GO

CREATE PROCEDURE dbo.sp_WorkPlaces_Update
    @Id UNIQUEIDENTIFIER,
    @Denumire NVARCHAR(200),
    @CodONRC NVARCHAR(50) = NULL,
    @TipPunctLucru TINYINT = 0,
    @RegimJuridic TINYINT = 0,
    @Adresa NVARCHAR(500),
    @Localitate NVARCHAR(100),
    @Judet NVARCHAR(50),
    @CodPostal NVARCHAR(10) = NULL,
    @Tara NVARCHAR(50) = N'România',
    @Telefon NVARCHAR(20) = NULL,
    @Email NVARCHAR(100) = NULL,
    @EsteSediuSocial BIT = 0,
    @DataInregistrare DATE = NULL,
    @DataRadiere DATE = NULL,
    @SortOrder INT = 0,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CompanyId UNIQUEIDENTIFIER;
    SELECT @CompanyId = CompanyId FROM WorkPlaces WHERE Id = @Id;
    
    -- If EsteSediuSocial = 1, check that no other active sediu social exists
    IF @EsteSediuSocial = 1 AND EXISTS (
        SELECT 1 FROM WorkPlaces 
        WHERE CompanyId = @CompanyId AND EsteSediuSocial = 1 AND IsActive = 1 AND Id <> @Id
    )
    BEGIN
        RAISERROR('Compania are deja un sediu social activ. Dezactivați-l înainte.', 16, 1);
        RETURN;
    END
    
    UPDATE WorkPlaces
    SET 
        Denumire = @Denumire,
        CodONRC = @CodONRC,
        TipPunctLucru = @TipPunctLucru,
        RegimJuridic = @RegimJuridic,
        Adresa = @Adresa,
        Localitate = @Localitate,
        Judet = @Judet,
        CodPostal = @CodPostal,
        Tara = @Tara,
        Telefon = @Telefon,
        Email = @Email,
        EsteSediuSocial = @EsteSediuSocial,
        DataInregistrare = @DataInregistrare,
        DataRadiere = @DataRadiere,
        SortOrder = @SortOrder,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_WorkPlaces_Update creat';
GO

-- Delete (Soft) WorkPlace
IF OBJECT_ID('dbo.sp_WorkPlaces_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_WorkPlaces_Delete;
GO

CREATE PROCEDURE dbo.sp_WorkPlaces_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Check if workplace has active locations
    IF EXISTS (SELECT 1 FROM Locations WHERE WorkPlaceId = @Id AND IsActive = 1)
    BEGIN
        RAISERROR('Nu se poate șterge punctul de lucru. Există locații active asociate.', 16, 1);
        RETURN;
    END
    
    UPDATE WorkPlaces
    SET 
        IsActive = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_WorkPlaces_Delete creat';
GO

-- =============================================
-- LOCATIONS - Stored Procedures
-- =============================================

-- Get All Locations
IF OBJECT_ID('dbo.sp_Locations_GetAll', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Locations_GetAll;
GO

CREATE PROCEDURE dbo.sp_Locations_GetAll
    @IncludeInactive BIT = 0,
    @CompanyId UNIQUEIDENTIFIER = NULL,
    @WorkPlaceId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        l.*,
        c.Denumire AS CompanyName,
        c.CUI AS CompanyCUI,
        wp.Denumire AS WorkPlaceName
    FROM Locations l
    INNER JOIN Companies c ON c.Id = l.CompanyId
    LEFT JOIN WorkPlaces wp ON wp.Id = l.WorkPlaceId
    WHERE (l.IsActive = 1 OR @IncludeInactive = 1)
      AND (@CompanyId IS NULL OR l.CompanyId = @CompanyId)
      AND (@WorkPlaceId IS NULL OR l.WorkPlaceId = @WorkPlaceId)
    ORDER BY l.SortOrder, l.Denumire;
END;
GO

PRINT '✅ sp_Locations_GetAll creat';
GO

-- Get Location By Id
IF OBJECT_ID('dbo.sp_Locations_GetById', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Locations_GetById;
GO

CREATE PROCEDURE dbo.sp_Locations_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        l.*,
        c.Denumire AS CompanyName,
        c.CUI AS CompanyCUI,
        wp.Denumire AS WorkPlaceName
    FROM Locations l
    INNER JOIN Companies c ON c.Id = l.CompanyId
    LEFT JOIN WorkPlaces wp ON wp.Id = l.WorkPlaceId
    WHERE l.Id = @Id;
END;
GO

PRINT '✅ sp_Locations_GetById creat';
GO

-- Create Location
IF OBJECT_ID('dbo.sp_Locations_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Locations_Create;
GO

CREATE PROCEDURE dbo.sp_Locations_Create
    @CompanyId UNIQUEIDENTIFIER,
    @WorkPlaceId UNIQUEIDENTIFIER = NULL,
    @CodIntern NVARCHAR(50),
    @Denumire NVARCHAR(200),
    @TipLocatie TINYINT = 0,
    @Adresa NVARCHAR(500) = NULL,
    @Localitate NVARCHAR(100) = NULL,
    @Judet NVARCHAR(50) = NULL,
    @CodPostal NVARCHAR(10) = NULL,
    @RegimJuridic TINYINT = 0,
    @HasStock BIT = 0,
    @CanSell BIT = 0,
    @CanPurchase BIT = 0,
    @CanManufacture BIT = 0,
    @Suprafata DECIMAL(12,2) = NULL,
    @Descriere NVARCHAR(500) = NULL,
    @SortOrder INT = 0,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Check if company exists
    IF NOT EXISTS (SELECT 1 FROM Companies WHERE Id = @CompanyId AND IsActive = 1)
    BEGIN
        RAISERROR('Compania specificată nu există sau este inactivă.', 16, 1);
        RETURN;
    END
    
    -- Check if workplace exists (if provided)
    IF @WorkPlaceId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM WorkPlaces WHERE Id = @WorkPlaceId AND IsActive = 1)
    BEGIN
        RAISERROR('Punctul de lucru specificat nu există sau este inactiv.', 16, 1);
        RETURN;
    END
    
    -- Check CodIntern uniqueness per company
    IF EXISTS (SELECT 1 FROM Locations WHERE CompanyId = @CompanyId AND CodIntern = @CodIntern)
    BEGIN
        RAISERROR('Codul intern %s există deja pentru această companie.', 16, 1, @CodIntern);
        RETURN;
    END
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    
    INSERT INTO Locations (
        Id, CompanyId, WorkPlaceId, CodIntern, Denumire, TipLocatie,
        Adresa, Localitate, Judet, CodPostal, RegimJuridic,
        HasStock, CanSell, CanPurchase, CanManufacture,
        Suprafata, Descriere, SortOrder, CreatedBy
    )
    VALUES (
        @NewId, @CompanyId, @WorkPlaceId, @CodIntern, @Denumire, @TipLocatie,
        @Adresa, @Localitate, @Judet, @CodPostal, @RegimJuridic,
        @HasStock, @CanSell, @CanPurchase, @CanManufacture,
        @Suprafata, @Descriere, @SortOrder, @CreatedBy
    );
    
    SELECT @NewId AS Id;
END;
GO

PRINT '✅ sp_Locations_Create creat';
GO

-- Update Location
IF OBJECT_ID('dbo.sp_Locations_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Locations_Update;
GO

CREATE PROCEDURE dbo.sp_Locations_Update
    @Id UNIQUEIDENTIFIER,
    @WorkPlaceId UNIQUEIDENTIFIER = NULL,
    @CodIntern NVARCHAR(50),
    @Denumire NVARCHAR(200),
    @TipLocatie TINYINT = 0,
    @Adresa NVARCHAR(500) = NULL,
    @Localitate NVARCHAR(100) = NULL,
    @Judet NVARCHAR(50) = NULL,
    @CodPostal NVARCHAR(10) = NULL,
    @RegimJuridic TINYINT = 0,
    @HasStock BIT = 0,
    @CanSell BIT = 0,
    @CanPurchase BIT = 0,
    @CanManufacture BIT = 0,
    @Suprafata DECIMAL(12,2) = NULL,
    @Descriere NVARCHAR(500) = NULL,
    @SortOrder INT = 0,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CompanyId UNIQUEIDENTIFIER;
    SELECT @CompanyId = CompanyId FROM Locations WHERE Id = @Id;
    
    -- Check CodIntern uniqueness (exclude current record)
    IF EXISTS (SELECT 1 FROM Locations WHERE CompanyId = @CompanyId AND CodIntern = @CodIntern AND Id <> @Id)
    BEGIN
        RAISERROR('Codul intern %s există deja pentru această companie.', 16, 1, @CodIntern);
        RETURN;
    END
    
    UPDATE Locations
    SET 
        WorkPlaceId = @WorkPlaceId,
        CodIntern = @CodIntern,
        Denumire = @Denumire,
        TipLocatie = @TipLocatie,
        Adresa = @Adresa,
        Localitate = @Localitate,
        Judet = @Judet,
        CodPostal = @CodPostal,
        RegimJuridic = @RegimJuridic,
        HasStock = @HasStock,
        CanSell = @CanSell,
        CanPurchase = @CanPurchase,
        CanManufacture = @CanManufacture,
        Suprafata = @Suprafata,
        Descriere = @Descriere,
        SortOrder = @SortOrder,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_Locations_Update creat';
GO

-- Delete (Soft) Location
IF OBJECT_ID('dbo.sp_Locations_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Locations_Delete;
GO

CREATE PROCEDURE dbo.sp_Locations_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE Locations
    SET 
        IsActive = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_Locations_Delete creat';
GO

-- =============================================
-- ORGANIZATION TREE - Stored Procedure
-- =============================================

-- Get Organization Tree for UI
IF OBJECT_ID('dbo.sp_Organization_GetTree', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Organization_GetTree;
GO

CREATE PROCEDURE dbo.sp_Organization_GetTree
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM vw_OrganizationTree
    ORDER BY [Level], SortOrder, Denumire;
END;
GO

PRINT '✅ sp_Organization_GetTree creat';
GO

PRINT '=== Migrare 018_StoredProcedures_SocietateaProprie completă ===';
GO
