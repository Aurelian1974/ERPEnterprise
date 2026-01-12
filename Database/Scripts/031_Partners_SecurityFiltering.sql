-- =============================================
-- ValyanERP - Partners Security Filtering
-- Script: 031_Partners_SecurityFiltering.sql
-- Data: 12 Ianuarie 2026
-- Descriere: Adaugă filtrare pe perimetru organizațional pentru Partners
--            Folosește Table-Valued Parameters pentru eficiență maximă
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 031_Partners_SecurityFiltering ===';
GO

-- =============================================
-- 1. Creează tipul de tabelă pentru GUID-uri (dacă nu există)
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.types WHERE name = 'GuidListType' AND is_table_type = 1)
BEGIN
    CREATE TYPE [dbo].[GuidListType] AS TABLE
    (
        [Id] UNIQUEIDENTIFIER NOT NULL
    );
    PRINT '✅ Tip GuidListType creat';
END
ELSE
BEGIN
    PRINT 'ℹ️ Tip GuidListType există deja';
END
GO

-- =============================================
-- 2. Recreate sp_Partners_GetAll cu filtrare pe perimetru
-- =============================================
IF OBJECT_ID('dbo.sp_Partners_GetAll_Filtered', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Partners_GetAll_Filtered;
GO

CREATE PROCEDURE dbo.sp_Partners_GetAll_Filtered
    @Skip INT = 0,
    @Take INT = 50,
    @IncludeInactive BIT = 0,
    @Categoria TINYINT = NULL,
    @RolPartener INT = NULL,
    @TipEntitate NVARCHAR(20) = NULL,
    @HasFullAccess BIT = 0,
    @VisibleCompanyIds GuidListType READONLY,
    @VisibleLocationIds GuidListType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Declare total count
    DECLARE @TotalCount INT;
    
    -- Get total count with filters and security
    -- Security logic:
    -- 1. If HasFullAccess → see all
    -- 2. If partner has OwnerLocationId → user must have access to that LOCATION
    -- 3. If partner has only OwnerCompanyId (no location) → user must have access to that COMPANY
    -- 4. Partners without any owner → only admins can see
    SELECT @TotalCount = COUNT(*)
    FROM vw_Partners p
    WHERE (p.IsActive = 1 OR @IncludeInactive = 1)
      AND (@Categoria IS NULL OR p.Categoria = @Categoria)
      AND (@RolPartener IS NULL OR (p.RolPartener & @RolPartener) = @RolPartener)
      AND (@TipEntitate IS NULL OR p.TipEntitate = @TipEntitate)
      AND (
          @HasFullAccess = 1
          OR (
              -- Partner has location assigned → must match user's visible locations
              (p.OwnerLocationId IS NOT NULL AND p.OwnerLocationId IN (SELECT Id FROM @VisibleLocationIds))
              OR
              -- Partner has only company (no location) → must match user's visible companies
              (p.OwnerLocationId IS NULL AND p.OwnerCompanyId IS NOT NULL AND p.OwnerCompanyId IN (SELECT Id FROM @VisibleCompanyIds))
          )
      );
    
    -- Get paged results with security filter
    SELECT 
        p.Id,
        p.Cod,
        p.Categoria,
        p.TipEntitate,
        p.RolPartener,
        p.PartnerStatus,
        p.DenumireAfisare,
        p.Denumire,
        p.DenumireScurta,
        p.Nume,
        p.Prenume,
        p.IdentificatorFiscal,
        p.CUI,
        p.CIF,
        p.CNP,
        p.VATID,
        p.RegCom,
        p.Email,
        p.Telefon,
        p.Website,
        p.TaraOrigine,
        p.EstePlatitorTVA,
        p.EsteActiv,
        p.EsteVerificat,
        p.AnafStatus,
        p.AnafVerifiedAt,
        p.BlocatFacturare,
        p.BlocatLivrare,
        p.LimitaCredit,
        p.TermenPlataDef,
        p.CategorieComercialaTxt AS CategorieComercială,
        p.CodPartenerSAFT,
        p.TipPartenerSAFT,
        p.AdresaPrincipala,
        p.Localitate,
        p.Judet,
        p.CodPostal,
        p.Tara,
        p.NrAdrese,
        p.NrContacte,
        p.NrConturi,
        p.NrReprezentanti,
        p.IsActive,
        p.CreatedAt,
        p.UpdatedAt,
        p.CreatedByUserName,
        p.OwnerCompanyId,
        p.OwnerWorkPlaceId,
        p.OwnerLocationId,
        p.OwnerCompanyName,
        p.OwnerLocationName
    FROM vw_Partners p
    WHERE (p.IsActive = 1 OR @IncludeInactive = 1)
      AND (@Categoria IS NULL OR p.Categoria = @Categoria)
      AND (@RolPartener IS NULL OR (p.RolPartener & @RolPartener) = @RolPartener)
      AND (@TipEntitate IS NULL OR p.TipEntitate = @TipEntitate)
      AND (
          @HasFullAccess = 1
          OR (
              (p.OwnerLocationId IS NOT NULL AND p.OwnerLocationId IN (SELECT Id FROM @VisibleLocationIds))
              OR
              (p.OwnerLocationId IS NULL AND p.OwnerCompanyId IS NOT NULL AND p.OwnerCompanyId IN (SELECT Id FROM @VisibleCompanyIds))
          )
      )
    ORDER BY p.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
    
    -- Return total count as second result set
    SELECT @TotalCount AS TotalCount;
END;
GO

PRINT '✅ sp_Partners_GetAll_Filtered creat cu filtrare pe perimetru';
GO

-- =============================================
-- 3. sp_Partners_Search cu filtrare pe perimetru
-- =============================================
IF OBJECT_ID('dbo.sp_Partners_Search_Filtered', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Partners_Search_Filtered;
GO

CREATE PROCEDURE dbo.sp_Partners_Search_Filtered
    @SearchTerm NVARCHAR(100),
    @Skip INT = 0,
    @Take INT = 50,
    @HasFullAccess BIT = 0,
    @VisibleCompanyIds GuidListType READONLY,
    @VisibleLocationIds GuidListType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @TotalCount INT;
    
    -- Get total count with search and security
    SELECT @TotalCount = COUNT(*)
    FROM vw_Partners p
    WHERE p.IsActive = 1
      AND (
          p.DenumireAfisare LIKE '%' + @SearchTerm + '%'
          OR p.CUI LIKE '%' + @SearchTerm + '%'
          OR p.CNP LIKE '%' + @SearchTerm + '%'
          OR p.CIF LIKE '%' + @SearchTerm + '%'
          OR p.Email LIKE '%' + @SearchTerm + '%'
          OR p.Telefon LIKE '%' + @SearchTerm + '%'
      )
      AND (
          @HasFullAccess = 1
          OR (
              (p.OwnerLocationId IS NOT NULL AND p.OwnerLocationId IN (SELECT Id FROM @VisibleLocationIds))
              OR
              (p.OwnerLocationId IS NULL AND p.OwnerCompanyId IS NOT NULL AND p.OwnerCompanyId IN (SELECT Id FROM @VisibleCompanyIds))
          )
      );
    
    -- Get paged results
    SELECT 
        p.Id,
        p.Cod,
        p.Categoria,
        p.TipEntitate,
        p.RolPartener,
        p.PartnerStatus,
        p.DenumireAfisare,
        p.Denumire,
        p.DenumireScurta,
        p.Nume,
        p.Prenume,
        p.IdentificatorFiscal,
        p.CUI,
        p.CIF,
        p.CNP,
        p.VATID,
        p.RegCom,
        p.Email,
        p.Telefon,
        p.Website,
        p.TaraOrigine,
        p.EstePlatitorTVA,
        p.EsteActiv,
        p.EsteVerificat,
        p.AnafStatus,
        p.AnafVerifiedAt,
        p.BlocatFacturare,
        p.BlocatLivrare,
        p.LimitaCredit,
        p.TermenPlataDef,
        p.CategorieComercialaTxt AS CategorieComercială,
        p.CodPartenerSAFT,
        p.TipPartenerSAFT,
        p.AdresaPrincipala,
        p.Localitate,
        p.Judet,
        p.CodPostal,
        p.Tara,
        p.NrAdrese,
        p.NrContacte,
        p.NrConturi,
        p.NrReprezentanti,
        p.IsActive,
        p.CreatedAt,
        p.UpdatedAt,
        p.CreatedByUserName,
        p.OwnerCompanyId,
        p.OwnerWorkPlaceId,
        p.OwnerLocationId,
        p.OwnerCompanyName,
        p.OwnerLocationName
    FROM vw_Partners p
    WHERE p.IsActive = 1
      AND (
          p.DenumireAfisare LIKE '%' + @SearchTerm + '%'
          OR p.CUI LIKE '%' + @SearchTerm + '%'
          OR p.CNP LIKE '%' + @SearchTerm + '%'
          OR p.CIF LIKE '%' + @SearchTerm + '%'
          OR p.Email LIKE '%' + @SearchTerm + '%'
          OR p.Telefon LIKE '%' + @SearchTerm + '%'
      )
      AND (
          @HasFullAccess = 1
          OR (
              (p.OwnerLocationId IS NOT NULL AND p.OwnerLocationId IN (SELECT Id FROM @VisibleLocationIds))
              OR
              (p.OwnerLocationId IS NULL AND p.OwnerCompanyId IS NOT NULL AND p.OwnerCompanyId IN (SELECT Id FROM @VisibleCompanyIds))
          )
      )
    ORDER BY p.CreatedAt DESC
    OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
    
    SELECT @TotalCount AS TotalCount;
END;
GO

PRINT '✅ sp_Partners_Search_Filtered creat cu filtrare pe perimetru';
GO

-- =============================================
-- 4. sp_Partners_GetById cu verificare perimetru
-- =============================================
IF OBJECT_ID('dbo.sp_Partners_GetById_Filtered', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Partners_GetById_Filtered;
GO

CREATE PROCEDURE dbo.sp_Partners_GetById_Filtered
    @Id UNIQUEIDENTIFIER,
    @HasFullAccess BIT = 0,
    @VisibleCompanyIds GuidListType READONLY,
    @VisibleLocationIds GuidListType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT * 
    FROM vw_Partners p
    WHERE p.Id = @Id
      AND (
          @HasFullAccess = 1
          OR (
              (p.OwnerLocationId IS NOT NULL AND p.OwnerLocationId IN (SELECT Id FROM @VisibleLocationIds))
              OR
              (p.OwnerLocationId IS NULL AND p.OwnerCompanyId IS NOT NULL AND p.OwnerCompanyId IN (SELECT Id FROM @VisibleCompanyIds))
          )
      );
END;
GO

PRINT '✅ sp_Partners_GetById_Filtered creat cu verificare perimetru';
GO

PRINT '=== Migrare 031_Partners_SecurityFiltering completă ===';
GO
