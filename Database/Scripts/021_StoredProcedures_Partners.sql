-- =============================================
-- ValyanERP - Partners Stored Procedures
-- Script: 021_StoredProcedures_Partners.sql
-- Data: 12 Ianuarie 2026
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 021_StoredProcedures_Partners ===';
GO

-- =============================================
-- PARTNERS - CRUD Operations
-- =============================================

-- sp_Partners_GetAll - Cu suport pentru paginare
IF OBJECT_ID('dbo.sp_Partners_GetAll', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_GetAll;
GO

CREATE PROCEDURE dbo.sp_Partners_GetAll
    @Skip INT = 0,
    @Take INT = 50,
    @IncludeInactive BIT = 0,
    @Categoria TINYINT = NULL,
    @RolPartener INT = NULL,
    @TipEntitate NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Declare total count
    DECLARE @TotalCount INT;
    
    -- Get total count with filters
    SELECT @TotalCount = COUNT(*)
    FROM vw_Partners
    WHERE (IsActive = 1 OR @IncludeInactive = 1)
      AND (@Categoria IS NULL OR Categoria = @Categoria)
      AND (@RolPartener IS NULL OR (RolPartener & @RolPartener) = @RolPartener)
      AND (@TipEntitate IS NULL OR TipEntitate = @TipEntitate);
    
    -- Get paged results with all columns for PartnerListDto
    SELECT 
        Id,
        Cod,
        Categoria,
        TipEntitate,
        RolPartener,
        PartnerStatus,
        DenumireAfisare,
        Denumire,
        DenumireScurta,
        Nume,
        Prenume,
        IdentificatorFiscal,
        CUI,
        CIF,
        CNP,
        VATID,
        RegCom,
        Email,
        Telefon,
        Website,
        TaraOrigine,
        [EstePlătitorTVA] AS EstePlatitorTVA,
        EsteActiv,
        EsteVerificat,
        AnafStatus,
        AnafVerifiedAt,
        BlocatFacturare,
        BlocatLivrare,
        LimitaCredit,
        TermenPlataDef,
        [CategorieComercială] AS CategorieComercială,
        CodPartenerSAFT,
        TipPartenerSAFT,
        AdresaPrincipala,
        Localitate,
        Judet,
        CodPostal,
        Tara,
        NrAdrese,
        NrContacte,
        NrConturi,
        NrReprezentanti,
        IsActive,
        CreatedAt,
        UpdatedAt,
        CreatedByUserName
    FROM vw_Partners
    WHERE (IsActive = 1 OR @IncludeInactive = 1)
      AND (@Categoria IS NULL OR Categoria = @Categoria)
      AND (@RolPartener IS NULL OR (RolPartener & @RolPartener) = @RolPartener)
      AND (@TipEntitate IS NULL OR TipEntitate = @TipEntitate)
    ORDER BY DenumireAfisare
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
    
    -- Return total count as second result set
    SELECT @TotalCount AS TotalCount;
END;
GO

PRINT '✅ sp_Partners_GetAll creat (cu paginare)';
GO

-- sp_Partners_GetById
IF OBJECT_ID('dbo.sp_Partners_GetById', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_GetById;
GO

CREATE PROCEDURE dbo.sp_Partners_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM vw_Partners WHERE Id = @Id;
END;
GO

PRINT '✅ sp_Partners_GetById creat';
GO

-- sp_Partners_GetByCUI
IF OBJECT_ID('dbo.sp_Partners_GetByCUI', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_GetByCUI;
GO

CREATE PROCEDURE dbo.sp_Partners_GetByCUI
    @CUI NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM vw_Partners WHERE CUI = @CUI AND IsActive = 1;
END;
GO

PRINT '✅ sp_Partners_GetByCUI creat';
GO

-- sp_Partners_GetByCNP
IF OBJECT_ID('dbo.sp_Partners_GetByCNP', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_GetByCNP;
GO

CREATE PROCEDURE dbo.sp_Partners_GetByCNP
    @CNP NVARCHAR(13)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM vw_Partners WHERE CNP = @CNP AND IsActive = 1;
END;
GO

PRINT '✅ sp_Partners_GetByCNP creat';
GO

-- sp_Partners_Search - Cu suport pentru paginare
IF OBJECT_ID('dbo.sp_Partners_Search', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_Search;
GO

CREATE PROCEDURE dbo.sp_Partners_Search
    @SearchTerm NVARCHAR(100),
    @Skip INT = 0,
    @Take INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Declare total count
    DECLARE @TotalCount INT;
    
    -- Get total count with search
    SELECT @TotalCount = COUNT(*)
    FROM vw_Partners
    WHERE IsActive = 1
      AND (
          DenumireAfisare LIKE '%' + @SearchTerm + '%'
          OR CUI LIKE '%' + @SearchTerm + '%'
          OR CNP LIKE '%' + @SearchTerm + '%'
          OR CIF LIKE '%' + @SearchTerm + '%'
          OR Email LIKE '%' + @SearchTerm + '%'
          OR Telefon LIKE '%' + @SearchTerm + '%'
      );
    
    -- Get paged results with all columns for PartnerListDto
    SELECT 
        Id,
        Cod,
        Categoria,
        TipEntitate,
        RolPartener,
        PartnerStatus,
        DenumireAfisare,
        Denumire,
        DenumireScurta,
        Nume,
        Prenume,
        IdentificatorFiscal,
        CUI,
        CIF,
        CNP,
        VATID,
        RegCom,
        Email,
        Telefon,
        Website,
        TaraOrigine,
        [EstePlătitorTVA] AS EstePlatitorTVA,
        EsteActiv,
        EsteVerificat,
        AnafStatus,
        AnafVerifiedAt,
        BlocatFacturare,
        BlocatLivrare,
        LimitaCredit,
        TermenPlataDef,
        [CategorieComercială] AS CategorieComercială,
        CodPartenerSAFT,
        TipPartenerSAFT,
        AdresaPrincipala,
        Localitate,
        Judet,
        CodPostal,
        Tara,
        NrAdrese,
        NrContacte,
        NrConturi,
        NrReprezentanti,
        IsActive,
        CreatedAt,
        UpdatedAt,
        CreatedByUserName
    FROM vw_Partners
    WHERE IsActive = 1
      AND (
          DenumireAfisare LIKE '%' + @SearchTerm + '%'
          OR CUI LIKE '%' + @SearchTerm + '%'
          OR CNP LIKE '%' + @SearchTerm + '%'
          OR CIF LIKE '%' + @SearchTerm + '%'
          OR Email LIKE '%' + @SearchTerm + '%'
          OR Telefon LIKE '%' + @SearchTerm + '%'
      )
    ORDER BY DenumireAfisare
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
    
    -- Return total count as second result set
    SELECT @TotalCount AS TotalCount;
END;
GO

PRINT '✅ sp_Partners_Search creat (cu paginare)';
GO

PRINT '=== Secțiune 1/4 completă: Query procedures ===';
GO

-- =============================================
-- PARTNERS - Create/Update Operations
-- =============================================

-- sp_Partners_Create
IF OBJECT_ID('dbo.sp_Partners_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_Create;
GO

CREATE PROCEDURE dbo.sp_Partners_Create
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
    @VATID NVARCHAR(20) = NULL,
    @CodFiscalStrain NVARCHAR(50) = NULL,
    @RegCom NVARCHAR(50) = NULL,
    @NrAutorizatie NVARCHAR(50) = NULL,
    @Pasaport NVARCHAR(50) = NULL,
    @DataInregistrare DATE = NULL,
    @TaraOrigine NVARCHAR(3) = 'RO',
    @CAENPrincipal NVARCHAR(10) = NULL,
    @CapitalSocial DECIMAL(18,2) = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @TelefonSecundar NVARCHAR(20) = NULL,
    @Website NVARCHAR(200) = NULL,
    @EstePlătitorTVA BIT = 0,
    @DataInregistrareTVA DATE = NULL,
    @StatusSplitTVA BIT = 0,
    @LimitaCredit DECIMAL(18,2) = NULL,
    @TermenPlataDef INT = 30,
    @CategorieComercială VARCHAR(20) = NULL,
    @IdentificatorTemp VARCHAR(50) = NULL,
    @MotivLipsaIdentificator NVARCHAR(200) = NULL,
    @Observatii NVARCHAR(2000) = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Generare cod partener
    DECLARE @Cod NVARCHAR(20);
    EXEC sp_Partners_GenerateCode @Code = @Cod OUTPUT;
    
    -- Generare SAF-T codes
    DECLARE @CodSAFT VARCHAR(35) = @Cod;
    DECLARE @TipSAFT VARCHAR(10) = CASE 
        WHEN (@RolPartener & 3) = 3 THEN 'CS'  -- Client + Furnizor
        WHEN (@RolPartener & 2) = 2 THEN 'C'   -- Client
        WHEN (@RolPartener & 1) = 1 THEN 'S'   -- Furnizor (Supplier)
        ELSE 'O'                                -- Other
    END;
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    
    INSERT INTO Partners (
        Id, Cod, Categoria, TipEntitate, RolPartener,
        Denumire, DenumireScurta, Nume, Prenume,
        CNP, CUI, CIF, VATID, CodFiscalStrain, RegCom, NrAutorizatie, Pasaport,
        DataInregistrare, TaraOrigine, CAENPrincipal, CapitalSocial,
        Email, Telefon, TelefonSecundar, Website,
        EstePlătitorTVA, DataInregistrareTVA, StatusSplitTVA,
        LimitaCredit, TermenPlataDef, CategorieComercială,
        CodPartenerSAFT, TipPartenerSAFT,
        SupplierID, CustomerID,
        IdentificatorTemp, MotivLipsaIdentificator,
        Observatii, CreatedBy
    )
    VALUES (
        @NewId, @Cod, @Categoria, @TipEntitate, @RolPartener,
        @Denumire, @DenumireScurta, @Nume, @Prenume,
        @CNP, @CUI, @CIF, @VATID, @CodFiscalStrain, @RegCom, @NrAutorizatie, @Pasaport,
        @DataInregistrare, @TaraOrigine, @CAENPrincipal, @CapitalSocial,
        @Email, @Telefon, @TelefonSecundar, @Website,
        @EstePlătitorTVA, @DataInregistrareTVA, @StatusSplitTVA,
        @LimitaCredit, @TermenPlataDef, @CategorieComercială,
        @CodSAFT, @TipSAFT,
        CASE WHEN (@RolPartener & 1) = 1 THEN @CodSAFT ELSE NULL END,
        CASE WHEN (@RolPartener & 2) = 2 THEN @CodSAFT ELSE NULL END,
        @IdentificatorTemp, @MotivLipsaIdentificator,
        @Observatii, @CreatedBy
    );
    
    SELECT @NewId AS Id, @Cod AS Cod;
END;
GO

PRINT '✅ sp_Partners_Create creat';
GO

-- sp_Partners_Update
IF OBJECT_ID('dbo.sp_Partners_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_Update;
GO

CREATE PROCEDURE dbo.sp_Partners_Update
    @Id UNIQUEIDENTIFIER,
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
    @VATID NVARCHAR(20) = NULL,
    @CodFiscalStrain NVARCHAR(50) = NULL,
    @RegCom NVARCHAR(50) = NULL,
    @NrAutorizatie NVARCHAR(50) = NULL,
    @Pasaport NVARCHAR(50) = NULL,
    @DataInregistrare DATE = NULL,
    @DataRadiere DATE = NULL,
    @TaraOrigine NVARCHAR(3) = 'RO',
    @CAENPrincipal NVARCHAR(10) = NULL,
    @CapitalSocial DECIMAL(18,2) = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @TelefonSecundar NVARCHAR(20) = NULL,
    @Website NVARCHAR(200) = NULL,
    @EstePlătitorTVA BIT = 0,
    @DataInregistrareTVA DATE = NULL,
    @StatusSplitTVA BIT = 0,
    @PartnerStatus VARCHAR(20) = 'Activ',
    @EsteActiv BIT = 1,
    @BlocatFacturare BIT = 0,
    @BlocatLivrare BIT = 0,
    @MotivBlocare NVARCHAR(500) = NULL,
    @LimitaCredit DECIMAL(18,2) = NULL,
    @TermenPlataDef INT = 30,
    @CategorieComercială VARCHAR(20) = NULL,
    @IdentificatorTemp VARCHAR(50) = NULL,
    @MotivLipsaIdentificator NVARCHAR(200) = NULL,
    @Observatii NVARCHAR(2000) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Update SAF-T codes based on role
    DECLARE @TipSAFT VARCHAR(10) = CASE 
        WHEN (@RolPartener & 3) = 3 THEN 'CS'
        WHEN (@RolPartener & 2) = 2 THEN 'C'
        WHEN (@RolPartener & 1) = 1 THEN 'S'
        ELSE 'O'
    END;
    
    UPDATE Partners
    SET 
        Categoria = @Categoria,
        TipEntitate = @TipEntitate,
        RolPartener = @RolPartener,
        Denumire = @Denumire,
        DenumireScurta = @DenumireScurta,
        Nume = @Nume,
        Prenume = @Prenume,
        CNP = @CNP,
        CUI = @CUI,
        CIF = @CIF,
        VATID = @VATID,
        CodFiscalStrain = @CodFiscalStrain,
        RegCom = @RegCom,
        NrAutorizatie = @NrAutorizatie,
        Pasaport = @Pasaport,
        DataInregistrare = @DataInregistrare,
        DataRadiere = @DataRadiere,
        TaraOrigine = @TaraOrigine,
        CAENPrincipal = @CAENPrincipal,
        CapitalSocial = @CapitalSocial,
        Email = @Email,
        Telefon = @Telefon,
        TelefonSecundar = @TelefonSecundar,
        Website = @Website,
        EstePlătitorTVA = @EstePlătitorTVA,
        DataInregistrareTVA = @DataInregistrareTVA,
        StatusSplitTVA = @StatusSplitTVA,
        PartnerStatus = @PartnerStatus,
        EsteActiv = @EsteActiv,
        BlocatFacturare = @BlocatFacturare,
        BlocatLivrare = @BlocatLivrare,
        MotivBlocare = @MotivBlocare,
        LimitaCredit = @LimitaCredit,
        TermenPlataDef = @TermenPlataDef,
        CategorieComercială = @CategorieComercială,
        TipPartenerSAFT = @TipSAFT,
        SupplierID = CASE WHEN (@RolPartener & 1) = 1 THEN CodPartenerSAFT ELSE NULL END,
        CustomerID = CASE WHEN (@RolPartener & 2) = 2 THEN CodPartenerSAFT ELSE NULL END,
        IdentificatorTemp = @IdentificatorTemp,
        MotivLipsaIdentificator = @MotivLipsaIdentificator,
        Observatii = @Observatii,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_Partners_Update creat';
GO

PRINT '=== Secțiune 2/4 completă: Create/Update procedures ===';
GO

-- =============================================
-- PARTNERS - Delete & Status Operations
-- =============================================

-- sp_Partners_Delete (Soft Delete)
IF OBJECT_ID('dbo.sp_Partners_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_Delete;
GO

CREATE PROCEDURE dbo.sp_Partners_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE Partners
    SET 
        IsActive = 0,
        PartnerStatus = 'Inactiv',
        EsteActiv = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_Partners_Delete creat';
GO

-- sp_Partners_UpdateAnafStatus
IF OBJECT_ID('dbo.sp_Partners_UpdateAnafStatus', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_UpdateAnafStatus;
GO

CREATE PROCEDURE dbo.sp_Partners_UpdateAnafStatus
    @Id UNIQUEIDENTIFIER,
    @AnafStatus VARCHAR(20),
    @EstePlătitorTVA BIT,
    @StatusSplitTVA BIT,
    @VerifiedBy UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE Partners
    SET 
        EsteVerificat = 1,
        AnafStatus = @AnafStatus,
        AnafVerifiedAt = GETDATE(),
        AnafVerifiedBy = @VerifiedBy,
        EstePlătitorTVA = @EstePlătitorTVA,
        StatusSplitTVA = @StatusSplitTVA,
        UpdatedAt = GETDATE(),
        UpdatedBy = @VerifiedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_Partners_UpdateAnafStatus creat';
GO

-- sp_Partners_SetPrincipalAddress
IF OBJECT_ID('dbo.sp_Partners_SetPrincipalAddress', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_Partners_SetPrincipalAddress;
GO

CREATE PROCEDURE dbo.sp_Partners_SetPrincipalAddress
    @PartnerId UNIQUEIDENTIFIER,
    @AddressId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE Partners
    SET AdresaPrincipalaId = @AddressId
    WHERE Id = @PartnerId;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_Partners_SetPrincipalAddress creat';
GO

-- =============================================
-- ANAF CACHE - Operations
-- =============================================

-- sp_AnafCache_Get
IF OBJECT_ID('dbo.sp_AnafCache_Get', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_AnafCache_Get;
GO

CREATE PROCEDURE dbo.sp_AnafCache_Get
    @CUI NVARCHAR(20),
    @DataInterogare DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    IF @DataInterogare IS NULL
        SET @DataInterogare = CAST(GETDATE() AS DATE);
    
    SELECT TOP 1 *
    FROM AnafVerificationCache
    WHERE CUI = @CUI 
      AND DataInterogare = @DataInterogare
      AND ExpiresAt > GETDATE()
    ORDER BY VerifiedAt DESC;
END;
GO

PRINT '✅ sp_AnafCache_Get creat';
GO

-- sp_AnafCache_Save
IF OBJECT_ID('dbo.sp_AnafCache_Save', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_AnafCache_Save;
GO

CREATE PROCEDURE dbo.sp_AnafCache_Save
    @CUI NVARCHAR(20),
    @Denumire NVARCHAR(200) = NULL,
    @Adresa NVARCHAR(500) = NULL,
    @NrRegCom NVARCHAR(50) = NULL,
    @ScpTVA BIT = 0,
    @DataInregistrareTVA DATE = NULL,
    @DataAnulareTVA DATE = NULL,
    @StatusTVA VARCHAR(50) = NULL,
    @StatusSplitTVA BIT = 0,
    @DataInceputSplitTVA DATE = NULL,
    @StatusInactivi BIT = 0,
    @DataInactivare DATE = NULL,
    @StatusRoEFactura BIT = 0,
    @DataInceputRoEFactura DATE = NULL,
    @RawResponse NVARCHAR(MAX) = NULL,
    @DataInterogare DATE = NULL,
    @CacheDurationHours INT = 24,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    IF @DataInterogare IS NULL
        SET @DataInterogare = CAST(GETDATE() AS DATE);
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    DECLARE @ExpiresAt DATETIME2 = DATEADD(HOUR, @CacheDurationHours, GETDATE());
    
    INSERT INTO AnafVerificationCache (
        Id, CUI, Denumire, Adresa, NrRegCom,
        ScpTVA, DataInregistrareTVA, DataAnulareTVA, StatusTVA,
        StatusSplitTVA, DataInceputSplitTVA,
        StatusInactivi, DataInactivare,
        StatusRoEFactura, DataInceputRoEFactura,
        RawResponse, DataInterogare, ExpiresAt, CreatedBy
    )
    VALUES (
        @NewId, @CUI, @Denumire, @Adresa, @NrRegCom,
        @ScpTVA, @DataInregistrareTVA, @DataAnulareTVA, @StatusTVA,
        @StatusSplitTVA, @DataInceputSplitTVA,
        @StatusInactivi, @DataInactivare,
        @StatusRoEFactura, @DataInceputRoEFactura,
        @RawResponse, @DataInterogare, @ExpiresAt, @CreatedBy
    );
    
    SELECT @NewId AS Id;
END;
GO

PRINT '✅ sp_AnafCache_Save creat';
GO

-- sp_AnafCache_Cleanup
IF OBJECT_ID('dbo.sp_AnafCache_Cleanup', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_AnafCache_Cleanup;
GO

CREATE PROCEDURE dbo.sp_AnafCache_Cleanup
AS
BEGIN
    SET NOCOUNT ON;
    
    DELETE FROM AnafVerificationCache
    WHERE ExpiresAt < GETDATE();
    
    SELECT @@ROWCOUNT AS DeletedCount;
END;
GO

PRINT '✅ sp_AnafCache_Cleanup creat';
GO

PRINT '=== Secțiune 3/4 completă: Delete/ANAF procedures ===';
GO

-- =============================================
-- PARTNER ADDRESSES - CRUD Operations
-- =============================================

-- sp_PartnerAddresses_GetByPartnerId
IF OBJECT_ID('dbo.sp_PartnerAddresses_GetByPartnerId', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerAddresses_GetByPartnerId;
GO

CREATE PROCEDURE dbo.sp_PartnerAddresses_GetByPartnerId
    @PartnerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM PartnerAddresses
    WHERE PartnerId = @PartnerId AND IsActive = 1
    ORDER BY EstePrincipala DESC, SortOrder, CreatedAt;
END;
GO

PRINT '✅ sp_PartnerAddresses_GetByPartnerId creat';
GO

-- sp_PartnerAddresses_Create
IF OBJECT_ID('dbo.sp_PartnerAddresses_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerAddresses_Create;
GO

CREATE PROCEDURE dbo.sp_PartnerAddresses_Create
    @PartnerId UNIQUEIDENTIFIER,
    @TipAdresa TINYINT = 0,
    @Denumire NVARCHAR(100) = NULL,
    @Adresa NVARCHAR(500),
    @Localitate NVARCHAR(100),
    @Judet NVARCHAR(50),
    @CodPostal NVARCHAR(10) = NULL,
    @Tara NVARCHAR(50) = N'România',
    @CodTaraISO NVARCHAR(3) = N'RO',
    @Telefon NVARCHAR(20) = NULL,
    @Email NVARCHAR(256) = NULL,
    @PersoanaContact NVARCHAR(200) = NULL,
    @EstePrincipala BIT = 0,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    
    -- Dacă e principală, resetează celelalte
    IF @EstePrincipala = 1
    BEGIN
        UPDATE PartnerAddresses 
        SET EstePrincipala = 0 
        WHERE PartnerId = @PartnerId AND IsActive = 1;
    END
    
    INSERT INTO PartnerAddresses (
        Id, PartnerId, TipAdresa, Denumire,
        Adresa, Localitate, Judet, CodPostal, Tara, CodTaraISO,
        Telefon, Email, PersoanaContact, EstePrincipala, CreatedBy
    )
    VALUES (
        @NewId, @PartnerId, @TipAdresa, @Denumire,
        @Adresa, @Localitate, @Judet, @CodPostal, @Tara, @CodTaraISO,
        @Telefon, @Email, @PersoanaContact, @EstePrincipala, @CreatedBy
    );
    
    -- Actualizează adresa principală în Partners dacă e marcată
    IF @EstePrincipala = 1
    BEGIN
        UPDATE Partners SET AdresaPrincipalaId = @NewId WHERE Id = @PartnerId;
    END
    
    SELECT @NewId AS Id;
END;
GO

PRINT '✅ sp_PartnerAddresses_Create creat';
GO

-- sp_PartnerAddresses_Update
IF OBJECT_ID('dbo.sp_PartnerAddresses_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerAddresses_Update;
GO

CREATE PROCEDURE dbo.sp_PartnerAddresses_Update
    @Id UNIQUEIDENTIFIER,
    @TipAdresa TINYINT = 0,
    @Denumire NVARCHAR(100) = NULL,
    @Adresa NVARCHAR(500),
    @Localitate NVARCHAR(100),
    @Judet NVARCHAR(50),
    @CodPostal NVARCHAR(10) = NULL,
    @Tara NVARCHAR(50) = N'România',
    @CodTaraISO NVARCHAR(3) = N'RO',
    @Telefon NVARCHAR(20) = NULL,
    @Email NVARCHAR(256) = NULL,
    @PersoanaContact NVARCHAR(200) = NULL,
    @EstePrincipala BIT = 0,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @PartnerId UNIQUEIDENTIFIER;
    SELECT @PartnerId = PartnerId FROM PartnerAddresses WHERE Id = @Id;
    
    -- Dacă e principală, resetează celelalte
    IF @EstePrincipala = 1
    BEGIN
        UPDATE PartnerAddresses 
        SET EstePrincipala = 0 
        WHERE PartnerId = @PartnerId AND Id <> @Id AND IsActive = 1;
    END
    
    UPDATE PartnerAddresses
    SET 
        TipAdresa = @TipAdresa,
        Denumire = @Denumire,
        Adresa = @Adresa,
        Localitate = @Localitate,
        Judet = @Judet,
        CodPostal = @CodPostal,
        Tara = @Tara,
        CodTaraISO = @CodTaraISO,
        Telefon = @Telefon,
        Email = @Email,
        PersoanaContact = @PersoanaContact,
        EstePrincipala = @EstePrincipala,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    -- Actualizează adresa principală în Partners dacă e marcată
    IF @EstePrincipala = 1
    BEGIN
        UPDATE Partners SET AdresaPrincipalaId = @Id WHERE Id = @PartnerId;
    END
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_PartnerAddresses_Update creat';
GO

-- sp_PartnerAddresses_Delete
IF OBJECT_ID('dbo.sp_PartnerAddresses_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerAddresses_Delete;
GO

CREATE PROCEDURE dbo.sp_PartnerAddresses_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @PartnerId UNIQUEIDENTIFIER;
    SELECT @PartnerId = PartnerId FROM PartnerAddresses WHERE Id = @Id;
    
    UPDATE PartnerAddresses
    SET IsActive = 0, UpdatedAt = GETDATE(), UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    -- Resetează referința din Partners dacă era principală
    UPDATE Partners 
    SET AdresaPrincipalaId = NULL 
    WHERE Id = @PartnerId AND AdresaPrincipalaId = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_PartnerAddresses_Delete creat';
GO

-- =============================================
-- PARTNER CONTACTS - CRUD Operations
-- =============================================

-- sp_PartnerContacts_GetByPartnerId
IF OBJECT_ID('dbo.sp_PartnerContacts_GetByPartnerId', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerContacts_GetByPartnerId;
GO

CREATE PROCEDURE dbo.sp_PartnerContacts_GetByPartnerId
    @PartnerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM PartnerContacts
    WHERE PartnerId = @PartnerId AND IsActive = 1
    ORDER BY EstePrincipal DESC, SortOrder, Nume;
END;
GO

PRINT '✅ sp_PartnerContacts_GetByPartnerId creat';
GO

-- sp_PartnerContacts_Create
IF OBJECT_ID('dbo.sp_PartnerContacts_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerContacts_Create;
GO

CREATE PROCEDURE dbo.sp_PartnerContacts_Create
    @PartnerId UNIQUEIDENTIFIER,
    @Nume NVARCHAR(100),
    @Prenume NVARCHAR(100) = NULL,
    @Functie NVARCHAR(100) = NULL,
    @Departament NVARCHAR(100) = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @TelefonMobil NVARCHAR(20) = NULL,
    @EsteDecident BIT = 0,
    @EstePrincipal BIT = 0,
    @Observatii NVARCHAR(500) = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    
    IF @EstePrincipal = 1
    BEGIN
        UPDATE PartnerContacts SET EstePrincipal = 0 
        WHERE PartnerId = @PartnerId AND IsActive = 1;
    END
    
    INSERT INTO PartnerContacts (
        Id, PartnerId, Nume, Prenume, Functie, Departament,
        Email, Telefon, TelefonMobil, EsteDecident, EstePrincipal,
        Observatii, CreatedBy
    )
    VALUES (
        @NewId, @PartnerId, @Nume, @Prenume, @Functie, @Departament,
        @Email, @Telefon, @TelefonMobil, @EsteDecident, @EstePrincipal,
        @Observatii, @CreatedBy
    );
    
    SELECT @NewId AS Id;
END;
GO

PRINT '✅ sp_PartnerContacts_Create creat';
GO

-- sp_PartnerContacts_Update
IF OBJECT_ID('dbo.sp_PartnerContacts_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerContacts_Update;
GO

CREATE PROCEDURE dbo.sp_PartnerContacts_Update
    @Id UNIQUEIDENTIFIER,
    @Nume NVARCHAR(100),
    @Prenume NVARCHAR(100) = NULL,
    @Functie NVARCHAR(100) = NULL,
    @Departament NVARCHAR(100) = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @TelefonMobil NVARCHAR(20) = NULL,
    @EsteDecident BIT = 0,
    @EstePrincipal BIT = 0,
    @Observatii NVARCHAR(500) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @PartnerId UNIQUEIDENTIFIER;
    SELECT @PartnerId = PartnerId FROM PartnerContacts WHERE Id = @Id;
    
    IF @EstePrincipal = 1
    BEGIN
        UPDATE PartnerContacts SET EstePrincipal = 0 
        WHERE PartnerId = @PartnerId AND Id <> @Id AND IsActive = 1;
    END
    
    UPDATE PartnerContacts
    SET 
        Nume = @Nume,
        Prenume = @Prenume,
        Functie = @Functie,
        Departament = @Departament,
        Email = @Email,
        Telefon = @Telefon,
        TelefonMobil = @TelefonMobil,
        EsteDecident = @EsteDecident,
        EstePrincipal = @EstePrincipal,
        Observatii = @Observatii,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_PartnerContacts_Update creat';
GO

-- sp_PartnerContacts_Delete
IF OBJECT_ID('dbo.sp_PartnerContacts_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerContacts_Delete;
GO

CREATE PROCEDURE dbo.sp_PartnerContacts_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE PartnerContacts
    SET IsActive = 0, UpdatedAt = GETDATE(), UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_PartnerContacts_Delete creat';
GO

-- =============================================
-- PARTNER BANK ACCOUNTS - CRUD Operations
-- =============================================

-- sp_PartnerBankAccounts_GetByPartnerId
IF OBJECT_ID('dbo.sp_PartnerBankAccounts_GetByPartnerId', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerBankAccounts_GetByPartnerId;
GO

CREATE PROCEDURE dbo.sp_PartnerBankAccounts_GetByPartnerId
    @PartnerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM PartnerBankAccounts
    WHERE PartnerId = @PartnerId AND IsActive = 1
    ORDER BY EstePrincipal DESC, SortOrder, NumeBanca;
END;
GO

PRINT '✅ sp_PartnerBankAccounts_GetByPartnerId creat';
GO

-- sp_PartnerBankAccounts_Create
IF OBJECT_ID('dbo.sp_PartnerBankAccounts_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerBankAccounts_Create;
GO

CREATE PROCEDURE dbo.sp_PartnerBankAccounts_Create
    @PartnerId UNIQUEIDENTIFIER,
    @IBAN NVARCHAR(34),
    @BIC NVARCHAR(11) = NULL,
    @NumeBanca NVARCHAR(100),
    @Sucursala NVARCHAR(100) = NULL,
    @Moneda NVARCHAR(3) = N'RON',
    @TitularCont NVARCHAR(200) = NULL,
    @EstePrincipal BIT = 0,
    @Observatii NVARCHAR(500) = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    
    IF @EstePrincipal = 1
    BEGIN
        UPDATE PartnerBankAccounts SET EstePrincipal = 0 
        WHERE PartnerId = @PartnerId AND IsActive = 1;
    END
    
    INSERT INTO PartnerBankAccounts (
        Id, PartnerId, IBAN, BIC, NumeBanca, Sucursala,
        Moneda, TitularCont, EstePrincipal, Observatii, CreatedBy
    )
    VALUES (
        @NewId, @PartnerId, @IBAN, @BIC, @NumeBanca, @Sucursala,
        @Moneda, @TitularCont, @EstePrincipal, @Observatii, @CreatedBy
    );
    
    SELECT @NewId AS Id;
END;
GO

PRINT '✅ sp_PartnerBankAccounts_Create creat';
GO

-- sp_PartnerBankAccounts_Update
IF OBJECT_ID('dbo.sp_PartnerBankAccounts_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerBankAccounts_Update;
GO

CREATE PROCEDURE dbo.sp_PartnerBankAccounts_Update
    @Id UNIQUEIDENTIFIER,
    @IBAN NVARCHAR(34),
    @BIC NVARCHAR(11) = NULL,
    @NumeBanca NVARCHAR(100),
    @Sucursala NVARCHAR(100) = NULL,
    @Moneda NVARCHAR(3) = N'RON',
    @TitularCont NVARCHAR(200) = NULL,
    @EstePrincipal BIT = 0,
    @Observatii NVARCHAR(500) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @PartnerId UNIQUEIDENTIFIER;
    SELECT @PartnerId = PartnerId FROM PartnerBankAccounts WHERE Id = @Id;
    
    IF @EstePrincipal = 1
    BEGIN
        UPDATE PartnerBankAccounts SET EstePrincipal = 0 
        WHERE PartnerId = @PartnerId AND Id <> @Id AND IsActive = 1;
    END
    
    UPDATE PartnerBankAccounts
    SET 
        IBAN = @IBAN,
        BIC = @BIC,
        NumeBanca = @NumeBanca,
        Sucursala = @Sucursala,
        Moneda = @Moneda,
        TitularCont = @TitularCont,
        EstePrincipal = @EstePrincipal,
        Observatii = @Observatii,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_PartnerBankAccounts_Update creat';
GO

-- sp_PartnerBankAccounts_Delete
IF OBJECT_ID('dbo.sp_PartnerBankAccounts_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerBankAccounts_Delete;
GO

CREATE PROCEDURE dbo.sp_PartnerBankAccounts_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE PartnerBankAccounts
    SET IsActive = 0, UpdatedAt = GETDATE(), UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_PartnerBankAccounts_Delete creat';
GO

-- =============================================
-- PARTNER REPRESENTATIVES - CRUD Operations
-- =============================================

-- sp_PartnerRepresentatives_GetByPartnerId
IF OBJECT_ID('dbo.sp_PartnerRepresentatives_GetByPartnerId', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerRepresentatives_GetByPartnerId;
GO

CREATE PROCEDURE dbo.sp_PartnerRepresentatives_GetByPartnerId
    @PartnerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * FROM PartnerRepresentatives
    WHERE PartnerId = @PartnerId AND IsActive = 1
    ORDER BY TipReprezentant, SortOrder, Nume;
END;
GO

PRINT '✅ sp_PartnerRepresentatives_GetByPartnerId creat';
GO

-- sp_PartnerRepresentatives_Create
IF OBJECT_ID('dbo.sp_PartnerRepresentatives_Create', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerRepresentatives_Create;
GO

CREATE PROCEDURE dbo.sp_PartnerRepresentatives_Create
    @PartnerId UNIQUEIDENTIFIER,
    @PersoanaId UNIQUEIDENTIFIER = NULL,
    @Nume NVARCHAR(100),
    @Prenume NVARCHAR(100) = NULL,
    @CNP NVARCHAR(13) = NULL,
    @Functie NVARCHAR(100),
    @TipReprezentant TINYINT = 0,
    @ArePutereSemnatura BIT = 0,
    @LimitaSemnatura DECIMAL(18,2) = NULL,
    @DataNumire DATE = NULL,
    @DataExpirare DATE = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @Observatii NVARCHAR(500) = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    
    INSERT INTO PartnerRepresentatives (
        Id, PartnerId, PersoanaId, Nume, Prenume, CNP,
        Functie, TipReprezentant, ArePutereSemnatura, LimitaSemnatura,
        DataNumire, DataExpirare, Email, Telefon, Observatii, CreatedBy
    )
    VALUES (
        @NewId, @PartnerId, @PersoanaId, @Nume, @Prenume, @CNP,
        @Functie, @TipReprezentant, @ArePutereSemnatura, @LimitaSemnatura,
        @DataNumire, @DataExpirare, @Email, @Telefon, @Observatii, @CreatedBy
    );
    
    SELECT @NewId AS Id;
END;
GO

PRINT '✅ sp_PartnerRepresentatives_Create creat';
GO

-- sp_PartnerRepresentatives_Update
IF OBJECT_ID('dbo.sp_PartnerRepresentatives_Update', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerRepresentatives_Update;
GO

CREATE PROCEDURE dbo.sp_PartnerRepresentatives_Update
    @Id UNIQUEIDENTIFIER,
    @PersoanaId UNIQUEIDENTIFIER = NULL,
    @Nume NVARCHAR(100),
    @Prenume NVARCHAR(100) = NULL,
    @CNP NVARCHAR(13) = NULL,
    @Functie NVARCHAR(100),
    @TipReprezentant TINYINT = 0,
    @ArePutereSemnatura BIT = 0,
    @LimitaSemnatura DECIMAL(18,2) = NULL,
    @DataNumire DATE = NULL,
    @DataExpirare DATE = NULL,
    @Email NVARCHAR(256) = NULL,
    @Telefon NVARCHAR(20) = NULL,
    @Observatii NVARCHAR(500) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE PartnerRepresentatives
    SET 
        PersoanaId = @PersoanaId,
        Nume = @Nume,
        Prenume = @Prenume,
        CNP = @CNP,
        Functie = @Functie,
        TipReprezentant = @TipReprezentant,
        ArePutereSemnatura = @ArePutereSemnatura,
        LimitaSemnatura = @LimitaSemnatura,
        DataNumire = @DataNumire,
        DataExpirare = @DataExpirare,
        Email = @Email,
        Telefon = @Telefon,
        Observatii = @Observatii,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_PartnerRepresentatives_Update creat';
GO

-- sp_PartnerRepresentatives_Delete
IF OBJECT_ID('dbo.sp_PartnerRepresentatives_Delete', 'P') IS NOT NULL DROP PROCEDURE dbo.sp_PartnerRepresentatives_Delete;
GO

CREATE PROCEDURE dbo.sp_PartnerRepresentatives_Delete
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE PartnerRepresentatives
    SET IsActive = 0, UpdatedAt = GETDATE(), UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ sp_PartnerRepresentatives_Delete creat';
GO

PRINT '=== Secțiune 4/4 completă: Sub-entity procedures ===';
GO

-- =============================================
-- FINALIZARE
-- =============================================
PRINT '';
PRINT '=== Migrare 021_StoredProcedures_Partners finalizată! ===';
PRINT '';
PRINT 'Proceduri create:';
PRINT '  Partners: GetAll, GetById, GetByCUI, GetByCNP, Search, Create, Update, Delete';
PRINT '  Partners: UpdateAnafStatus, SetPrincipalAddress';
PRINT '  AnafCache: Get, Save, Cleanup';
PRINT '  PartnerAddresses: GetByPartnerId, Create, Update, Delete';
PRINT '  PartnerContacts: GetByPartnerId, Create, Update, Delete';
PRINT '  PartnerBankAccounts: GetByPartnerId, Create, Update, Delete';
PRINT '  PartnerRepresentatives: GetByPartnerId, Create, Update, Delete';
PRINT '';
GO
