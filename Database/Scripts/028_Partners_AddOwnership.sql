-- =============================================
-- ValyanERP - Add Ownership Columns to Partners
-- Script: 028_Partners_AddOwnership.sql
-- Data: 12 Ianuarie 2026
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 028_Partners_AddOwnership ===';
GO

-- =============================================
-- 1. Adaugă coloanele Owner în tabelul Partners
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Partners') AND name = 'OwnerCompanyId')
BEGIN
    ALTER TABLE [dbo].[Partners] ADD [OwnerCompanyId] UNIQUEIDENTIFIER NULL;
    PRINT '✅ Coloană OwnerCompanyId adăugată';
END
ELSE
BEGIN
    PRINT '⏭️ Coloană OwnerCompanyId există deja';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Partners') AND name = 'OwnerWorkPlaceId')
BEGIN
    ALTER TABLE [dbo].[Partners] ADD [OwnerWorkPlaceId] UNIQUEIDENTIFIER NULL;
    PRINT '✅ Coloană OwnerWorkPlaceId adăugată';
END
ELSE
BEGIN
    PRINT '⏭️ Coloană OwnerWorkPlaceId există deja';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Partners') AND name = 'OwnerLocationId')
BEGIN
    ALTER TABLE [dbo].[Partners] ADD [OwnerLocationId] UNIQUEIDENTIFIER NULL;
    PRINT '✅ Coloană OwnerLocationId adăugată';
END
ELSE
BEGIN
    PRINT '⏭️ Coloană OwnerLocationId există deja';
END
GO

-- =============================================
-- 2. Adaugă FK constraints
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Partners_OwnerCompany')
BEGIN
    ALTER TABLE [dbo].[Partners] 
    ADD CONSTRAINT [FK_Partners_OwnerCompany] 
    FOREIGN KEY ([OwnerCompanyId]) REFERENCES [dbo].[Companies]([Id]);
    PRINT '✅ FK_Partners_OwnerCompany adăugat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Partners_OwnerWorkPlace')
BEGIN
    ALTER TABLE [dbo].[Partners] 
    ADD CONSTRAINT [FK_Partners_OwnerWorkPlace] 
    FOREIGN KEY ([OwnerWorkPlaceId]) REFERENCES [dbo].[WorkPlaces]([Id]);
    PRINT '✅ FK_Partners_OwnerWorkPlace adăugat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Partners_OwnerLocation')
BEGIN
    ALTER TABLE [dbo].[Partners] 
    ADD CONSTRAINT [FK_Partners_OwnerLocation] 
    FOREIGN KEY ([OwnerLocationId]) REFERENCES [dbo].[Locations]([Id]);
    PRINT '✅ FK_Partners_OwnerLocation adăugat';
END
GO

-- =============================================
-- 3. Adaugă indexuri pentru filtrare
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Partners_OwnerCompanyId')
BEGIN
    CREATE INDEX [IX_Partners_OwnerCompanyId] ON [dbo].[Partners] ([OwnerCompanyId]) WHERE [OwnerCompanyId] IS NOT NULL;
    PRINT '✅ Index IX_Partners_OwnerCompanyId adăugat';
END
GO

-- =============================================
-- 4. Actualizează sp_Partners_Create să accepte Owner params
-- =============================================
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
    -- NEW: Ownership parameters
    @OwnerCompanyId UNIQUEIDENTIFIER = NULL,
    @OwnerWorkPlaceId UNIQUEIDENTIFIER = NULL,
    @OwnerLocationId UNIQUEIDENTIFIER = NULL,
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
        OwnerCompanyId, OwnerWorkPlaceId, OwnerLocationId,
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
        @OwnerCompanyId, @OwnerWorkPlaceId, @OwnerLocationId,
        @Observatii, @CreatedBy
    );
    
    SELECT @NewId AS Id, @Cod AS Cod;
END;
GO

PRINT '✅ sp_Partners_Create actualizat cu Owner params';
GO

PRINT '=== Migrare 028_Partners_AddOwnership COMPLETĂ ===';
GO
