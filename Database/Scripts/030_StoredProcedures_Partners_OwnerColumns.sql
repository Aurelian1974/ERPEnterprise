-- =============================================
-- ValyanERP - Update Partner SPs cu Owner Names
-- Script: 030_StoredProcedures_Partners_OwnerColumns.sql
-- Data: 12 Ianuarie 2026
-- Descriere: Actualizează sp_Partners_GetAll, sp_Partners_Search, sp_Partners_GetById
--            pentru a include OwnerCompanyName și OwnerLocationName
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 030_StoredProcedures_Partners_OwnerColumns ===';
GO

-- =============================================
-- 1. Recreate sp_Partners_GetAll cu Owner columns
-- =============================================
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
        EstePlatitorTVA,
        EsteActiv,
        EsteVerificat,
        AnafStatus,
        AnafVerifiedAt,
        BlocatFacturare,
        BlocatLivrare,
        LimitaCredit,
        TermenPlataDef,
        CategorieComercialaTxt AS CategorieComercială,
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
        CreatedByUserName,
        -- Owner columns (NEW)
        OwnerCompanyId,
        OwnerWorkPlaceId,
        OwnerLocationId,
        OwnerCompanyName,
        OwnerLocationName
    FROM vw_Partners
    WHERE (IsActive = 1 OR @IncludeInactive = 1)
      AND (@Categoria IS NULL OR Categoria = @Categoria)
      AND (@RolPartener IS NULL OR (RolPartener & @RolPartener) = @RolPartener)
      AND (@TipEntitate IS NULL OR TipEntitate = @TipEntitate)
    ORDER BY CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
    
    -- Return total count as second result set
    SELECT @TotalCount AS TotalCount;
END;
GO

PRINT '✅ sp_Partners_GetAll actualizat cu Owner columns';
GO

-- =============================================
-- 2. Recreate sp_Partners_GetById cu Owner columns
-- =============================================
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

PRINT '✅ sp_Partners_GetById actualizat (folosește SELECT * din vw_Partners)';
GO

-- =============================================
-- 3. Recreate sp_Partners_Search cu Owner columns
-- =============================================
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
        EstePlatitorTVA,
        EsteActiv,
        EsteVerificat,
        AnafStatus,
        AnafVerifiedAt,
        BlocatFacturare,
        BlocatLivrare,
        LimitaCredit,
        TermenPlataDef,
        CategorieComercialaTxt AS CategorieComercială,
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
        CreatedByUserName,
        -- Owner columns (NEW)
        OwnerCompanyId,
        OwnerWorkPlaceId,
        OwnerLocationId,
        OwnerCompanyName,
        OwnerLocationName
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
    ORDER BY CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
    
    -- Return total count as second result set
    SELECT @TotalCount AS TotalCount;
END;
GO

PRINT '✅ sp_Partners_Search actualizat cu Owner columns';
GO

PRINT '=== Migrare 030_StoredProcedures_Partners_OwnerColumns completă ===';
GO
