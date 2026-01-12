-- =============================================
-- ValyanERP - Societatea Proprie Tables
-- Script: 017_SocietateaProprie.sql
-- Data: 11 Ianuarie 2026
-- Descriere: Tabele pentru gestionarea structurii organizaționale
--            (Grupuri, Companii, Puncte de Lucru, Locații)
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 017_SocietateaProprie ===';
GO

-- =============================================
-- 1. GRUPURI DE COMPANII
-- =============================================
IF OBJECT_ID('dbo.CompanyGroups', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CompanyGroups] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [Denumire] NVARCHAR(200) NOT NULL,
        [Descriere] NVARCHAR(500) NULL,
        [LogoUrl] NVARCHAR(500) NULL,
        [Website] NVARCHAR(200) NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [SortOrder] INT NOT NULL DEFAULT 0,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL
    );
    
    PRINT '✅ Tabel CompanyGroups creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel CompanyGroups există deja';
END
GO

-- Index pentru CompanyGroups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CompanyGroups_IsActive')
BEGIN
    CREATE INDEX IX_CompanyGroups_IsActive ON CompanyGroups(IsActive) INCLUDE (Denumire, SortOrder);
    PRINT '✅ Index IX_CompanyGroups_IsActive creat';
END
GO

-- =============================================
-- 2. COMPANII (fără duplicare sediu social!)
-- =============================================
IF OBJECT_ID('dbo.Companies', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Companies] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [GroupId] UNIQUEIDENTIFIER NULL,
        [ParentCompanyId] UNIQUEIDENTIFIER NULL,
        
        -- Identificare fiscală
        [CUI] NVARCHAR(20) NOT NULL,
        [RegCom] NVARCHAR(50) NULL,
        [Denumire] NVARCHAR(200) NOT NULL,
        [DenumireScurta] NVARCHAR(50) NULL,
        
        -- Tip companie în grup
        [TipCompanie] TINYINT NOT NULL DEFAULT 0, -- 0=Independent, 1=Holding, 2=Subsidiary
        [ProcentDetinere] DECIMAL(5,2) NULL, -- NULL pentru holding, % pentru subsidiary
        [CapitalSocial] DECIMAL(18,2) NULL,
        
        -- Contact (la nivel de companie, nu sediu)
        [Telefon] NVARCHAR(20) NULL,
        [Email] NVARCHAR(100) NULL,
        [Website] NVARCHAR(200) NULL,
        [LogoUrl] NVARCHAR(500) NULL,
        
        -- Flags
        [IsPrincipal] BIT NOT NULL DEFAULT 0, -- Compania principală din grup
        [IsActive] BIT NOT NULL DEFAULT 1,
        [SortOrder] INT NOT NULL DEFAULT 0,
        
        -- Audit
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [UQ_Companies_CUI] UNIQUE ([CUI]),
        CONSTRAINT [FK_Companies_Group] FOREIGN KEY ([GroupId]) REFERENCES [dbo].[CompanyGroups]([Id]),
        CONSTRAINT [FK_Companies_Parent] FOREIGN KEY ([ParentCompanyId]) REFERENCES [dbo].[Companies]([Id]),
        
        -- Previne self-reference directă (A -> A)
        CONSTRAINT [CK_Companies_NoSelfReference] CHECK ([ParentCompanyId] <> [Id])
    );
    
    PRINT '✅ Tabel Companies creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel Companies există deja';
END
GO

-- Indexuri pentru Companies
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Companies_GroupId')
BEGIN
    CREATE INDEX IX_Companies_GroupId ON Companies(GroupId) WHERE GroupId IS NOT NULL;
    PRINT '✅ Index IX_Companies_GroupId creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Companies_ParentCompanyId')
BEGIN
    CREATE INDEX IX_Companies_ParentCompanyId ON Companies(ParentCompanyId) WHERE ParentCompanyId IS NOT NULL;
    PRINT '✅ Index IX_Companies_ParentCompanyId creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Companies_CUI')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX IX_Companies_CUI ON Companies(CUI);
    PRINT '✅ Index IX_Companies_CUI creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Companies_IsActive')
BEGIN
    CREATE INDEX IX_Companies_IsActive ON Companies(IsActive) INCLUDE (Denumire, CUI, TipCompanie, SortOrder);
    PRINT '✅ Index IX_Companies_IsActive creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Companies_TipCompanie')
BEGIN
    CREATE INDEX IX_Companies_TipCompanie ON Companies(TipCompanie) WHERE IsActive = 1;
    PRINT '✅ Index IX_Companies_TipCompanie creat';
END
GO

-- =============================================
-- 3. PUNCTE DE LUCRU (cu RegimJuridic!)
-- =============================================
IF OBJECT_ID('dbo.WorkPlaces', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[WorkPlaces] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [CompanyId] UNIQUEIDENTIFIER NOT NULL,
        
        -- Identificare ONRC
        [CodONRC] NVARCHAR(50) NULL,
        [Denumire] NVARCHAR(200) NOT NULL,
        
        -- Tip punct de lucru
        [TipPunctLucru] TINYINT NOT NULL DEFAULT 0, -- 0=SediuSocial, 1=Sucursala, 2=Agentie, 3=PunctLucru
        
        -- Regim juridic (sediul poate fi închiriat!)
        [RegimJuridic] TINYINT NOT NULL DEFAULT 0, -- 0=Proprietate, 1=Inchiriere, 2=Comodat, 3=Leasing
        
        -- Adresa
        [Adresa] NVARCHAR(500) NOT NULL,
        [Localitate] NVARCHAR(100) NOT NULL,
        [Judet] NVARCHAR(50) NOT NULL,
        [CodPostal] NVARCHAR(10) NULL,
        [Tara] NVARCHAR(50) NOT NULL DEFAULT N'România',
        
        -- Contact
        [Telefon] NVARCHAR(20) NULL,
        [Email] NVARCHAR(100) NULL,
        
        -- Flags
        [EsteSediuSocial] BIT NOT NULL DEFAULT 0,
        [IsActive] BIT NOT NULL DEFAULT 1,
        
        -- Date ONRC
        [DataInregistrare] DATE NULL,
        [DataRadiere] DATE NULL,
        
        -- Ordering
        [SortOrder] INT NOT NULL DEFAULT 0,
        
        -- Audit
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [FK_WorkPlaces_Company] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies]([Id])
    );
    
    PRINT '✅ Tabel WorkPlaces creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel WorkPlaces există deja';
END
GO

-- Constraint: O singură locație ca sediu social per companie (UNIQUE filtered index)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_WorkPlaces_SediuSocial_PerCompany')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_WorkPlaces_SediuSocial_PerCompany]
    ON [dbo].[WorkPlaces] ([CompanyId])
    WHERE [EsteSediuSocial] = 1 AND [IsActive] = 1;
    PRINT '✅ Index UQ_WorkPlaces_SediuSocial_PerCompany creat';
END
GO

-- Indexuri pentru WorkPlaces
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkPlaces_CompanyId')
BEGIN
    CREATE INDEX IX_WorkPlaces_CompanyId ON WorkPlaces(CompanyId);
    PRINT '✅ Index IX_WorkPlaces_CompanyId creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkPlaces_CodONRC')
BEGIN
    CREATE INDEX IX_WorkPlaces_CodONRC ON WorkPlaces(CodONRC) WHERE CodONRC IS NOT NULL;
    PRINT '✅ Index IX_WorkPlaces_CodONRC creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkPlaces_EsteSediuSocial')
BEGIN
    CREATE INDEX IX_WorkPlaces_EsteSediuSocial ON WorkPlaces(EsteSediuSocial, CompanyId) WHERE IsActive = 1;
    PRINT '✅ Index IX_WorkPlaces_EsteSediuSocial creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_WorkPlaces_IsActive')
BEGIN
    CREATE INDEX IX_WorkPlaces_IsActive ON WorkPlaces(IsActive) INCLUDE (Denumire, TipPunctLucru, SortOrder);
    PRINT '✅ Index IX_WorkPlaces_IsActive creat';
END
GO

-- =============================================
-- 4. LOCAȚII
-- =============================================
IF OBJECT_ID('dbo.Locations', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Locations] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [CompanyId] UNIQUEIDENTIFIER NOT NULL,
        [WorkPlaceId] UNIQUEIDENTIFIER NULL, -- NULL = LOC-ONLY
        
        -- Identificare
        [CodIntern] NVARCHAR(50) NOT NULL,
        [Denumire] NVARCHAR(200) NOT NULL,
        
        -- Tip locație
        [TipLocatie] TINYINT NOT NULL DEFAULT 0, 
        -- 0=Depozit, 1=Magazin, 2=Birou, 3=Showroom, 4=Teren, 5=Service, 6=Parcare, 7=Altele
        
        -- Adresa (poate fi diferită de punctul de lucru)
        [Adresa] NVARCHAR(500) NULL,
        [Localitate] NVARCHAR(100) NULL,
        [Judet] NVARCHAR(50) NULL,
        [CodPostal] NVARCHAR(10) NULL,
        
        -- Regim juridic
        [RegimJuridic] TINYINT NOT NULL DEFAULT 0,
        -- 0=Proprietate, 1=Inchiriere, 2=Comodat, 3=Leasing
        
        -- Capabilități ERP
        [HasStock] BIT NOT NULL DEFAULT 0,      -- Poate gestiona stoc
        [CanSell] BIT NOT NULL DEFAULT 0,       -- Poate emite facturi de vânzare
        [CanPurchase] BIT NOT NULL DEFAULT 0,   -- Poate recepționa achiziții
        [CanManufacture] BIT NOT NULL DEFAULT 0, -- Poate produce
        
        -- Detalii
        [Suprafata] DECIMAL(12,2) NULL, -- în m²
        [Descriere] NVARCHAR(500) NULL,
        
        -- Flags
        [IsActive] BIT NOT NULL DEFAULT 1,
        [SortOrder] INT NOT NULL DEFAULT 0,
        
        -- Audit
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [FK_Locations_Company] FOREIGN KEY ([CompanyId]) REFERENCES [dbo].[Companies]([Id]),
        CONSTRAINT [FK_Locations_WorkPlace] FOREIGN KEY ([WorkPlaceId]) REFERENCES [dbo].[WorkPlaces]([Id]),
        CONSTRAINT [UQ_Locations_CodIntern_Company] UNIQUE ([CompanyId], [CodIntern])
    );
    
    PRINT '✅ Tabel Locations creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel Locations există deja';
END
GO

-- Indexuri pentru Locations
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Locations_CompanyId')
BEGIN
    CREATE INDEX IX_Locations_CompanyId ON Locations(CompanyId);
    PRINT '✅ Index IX_Locations_CompanyId creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Locations_WorkPlaceId')
BEGIN
    CREATE INDEX IX_Locations_WorkPlaceId ON Locations(WorkPlaceId) WHERE WorkPlaceId IS NOT NULL;
    PRINT '✅ Index IX_Locations_WorkPlaceId creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Locations_CodIntern')
BEGIN
    CREATE INDEX IX_Locations_CodIntern ON Locations(CodIntern, CompanyId);
    PRINT '✅ Index IX_Locations_CodIntern creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Locations_TipLocatie')
BEGIN
    CREATE INDEX IX_Locations_TipLocatie ON Locations(TipLocatie) WHERE IsActive = 1;
    PRINT '✅ Index IX_Locations_TipLocatie creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Locations_Capabilities')
BEGIN
    CREATE INDEX IX_Locations_Capabilities ON Locations(HasStock, CanSell, CanPurchase) WHERE IsActive = 1;
    PRINT '✅ Index IX_Locations_Capabilities creat';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Locations_IsActive')
BEGIN
    CREATE INDEX IX_Locations_IsActive ON Locations(IsActive) INCLUDE (Denumire, TipLocatie, SortOrder);
    PRINT '✅ Index IX_Locations_IsActive creat';
END
GO

-- =============================================
-- VIEW: Company cu Sediu Social (Single Source of Truth)
-- =============================================
IF OBJECT_ID('dbo.vw_CompanyWithSediuSocial', 'V') IS NOT NULL
    DROP VIEW dbo.vw_CompanyWithSediuSocial;
GO

CREATE VIEW [dbo].[vw_CompanyWithSediuSocial] AS
SELECT 
    c.Id,
    c.CUI,
    c.Denumire,
    c.DenumireScurta,
    c.RegCom,
    c.TipCompanie,
    c.ProcentDetinere,
    c.CapitalSocial,
    c.GroupId,
    c.ParentCompanyId,
    c.Telefon,
    c.Email,
    c.Website,
    c.LogoUrl,
    c.IsPrincipal,
    c.IsActive,
    c.SortOrder,
    c.CreatedAt,
    c.CreatedBy,
    c.UpdatedAt,
    c.UpdatedBy,
    -- Sediu Social din WorkPlace (Single Source of Truth!)
    wp.Id AS SediuSocialId,
    wp.Adresa AS SediuSocialAdresa,
    wp.Localitate AS SediuSocialLocalitate,
    wp.Judet AS SediuSocialJudet,
    wp.CodPostal AS SediuSocialCodPostal,
    wp.Tara AS SediuSocialTara,
    wp.RegimJuridic AS SediuSocialRegimJuridic,
    wp.CodONRC AS SediuSocialCodONRC,
    -- Group info
    g.Denumire AS GroupName,
    -- Parent company info
    pc.Denumire AS ParentCompanyName,
    -- Counts
    (SELECT COUNT(*) FROM WorkPlaces wp2 WHERE wp2.CompanyId = c.Id AND wp2.IsActive = 1) AS WorkPlaceCount,
    (SELECT COUNT(*) FROM Locations l WHERE l.CompanyId = c.Id AND l.IsActive = 1) AS LocationCount
FROM Companies c
LEFT JOIN WorkPlaces wp ON wp.CompanyId = c.Id 
    AND wp.EsteSediuSocial = 1 
    AND wp.IsActive = 1
LEFT JOIN CompanyGroups g ON g.Id = c.GroupId
LEFT JOIN Companies pc ON pc.Id = c.ParentCompanyId;
GO

PRINT '✅ View vw_CompanyWithSediuSocial creat cu succes';
GO

-- =============================================
-- VIEW: Organization Tree pentru TreeView UI
-- =============================================
IF OBJECT_ID('dbo.vw_OrganizationTree', 'V') IS NOT NULL
    DROP VIEW dbo.vw_OrganizationTree;
GO

CREATE VIEW [dbo].[vw_OrganizationTree] AS
-- Grupuri
SELECT 
    'GROUP' AS NodeType,
    CAST(g.Id AS NVARCHAR(50)) AS NodeId,
    NULL AS ParentId,
    g.Denumire AS Denumire,
    NULL AS Cod,
    NULL AS TipNode,
    0 AS [Level],
    g.IsActive,
    g.SortOrder,
    0 AS HasStock,
    0 AS CanSell,
    0 AS CanPurchase
FROM CompanyGroups g
WHERE g.IsActive = 1

UNION ALL

-- Companii
SELECT 
    'COMPANY' AS NodeType,
    CAST(c.Id AS NVARCHAR(50)) AS NodeId,
    CAST(COALESCE(c.GroupId, c.ParentCompanyId) AS NVARCHAR(50)) AS ParentId,
    c.Denumire + ' (' + c.CUI + ')' AS Denumire,
    c.CUI AS Cod,
    CASE c.TipCompanie 
        WHEN 0 THEN 'Independent'
        WHEN 1 THEN 'Holding'
        WHEN 2 THEN 'Subsidiary'
    END AS TipNode,
    1 AS [Level],
    c.IsActive,
    c.SortOrder,
    0 AS HasStock,
    0 AS CanSell,
    0 AS CanPurchase
FROM Companies c
WHERE c.IsActive = 1

UNION ALL

-- Puncte de Lucru
SELECT 
    'WORKPLACE' AS NodeType,
    CAST(wp.Id AS NVARCHAR(50)) AS NodeId,
    CAST(wp.CompanyId AS NVARCHAR(50)) AS ParentId,
    wp.Denumire AS Denumire,
    wp.CodONRC AS Cod,
    CASE wp.TipPunctLucru
        WHEN 0 THEN 'Sediu Social'
        WHEN 1 THEN N'Sucursală'
        WHEN 2 THEN N'Agenție'
        WHEN 3 THEN 'Punct Lucru'
    END AS TipNode,
    2 AS [Level],
    wp.IsActive,
    wp.SortOrder,
    0 AS HasStock,
    0 AS CanSell,
    0 AS CanPurchase
FROM WorkPlaces wp
WHERE wp.IsActive = 1

UNION ALL

-- Locații
SELECT 
    'LOCATION' AS NodeType,
    CAST(l.Id AS NVARCHAR(50)) AS NodeId,
    CAST(COALESCE(l.WorkPlaceId, l.CompanyId) AS NVARCHAR(50)) AS ParentId,
    l.Denumire AS Denumire,
    l.CodIntern AS Cod,
    CASE l.TipLocatie
        WHEN 0 THEN 'Depozit'
        WHEN 1 THEN 'Magazin'
        WHEN 2 THEN 'Birou'
        WHEN 3 THEN 'Showroom'
        WHEN 4 THEN 'Teren'
        WHEN 5 THEN 'Service'
        WHEN 6 THEN 'Parcare'
        WHEN 7 THEN 'Altele'
    END AS TipNode,
    CASE WHEN l.WorkPlaceId IS NULL THEN 2 ELSE 3 END AS [Level],
    l.IsActive,
    l.SortOrder,
    CAST(l.HasStock AS INT) AS HasStock,
    CAST(l.CanSell AS INT) AS CanSell,
    CAST(l.CanPurchase AS INT) AS CanPurchase
FROM Locations l
WHERE l.IsActive = 1;
GO

PRINT '✅ View vw_OrganizationTree creat cu succes';
GO

-- =============================================
-- FUNCȚIE: Validare cicluri ParentCompanyId
-- =============================================
IF OBJECT_ID('dbo.fn_Companies_HasCycle', 'FN') IS NOT NULL
    DROP FUNCTION dbo.fn_Companies_HasCycle;
GO

CREATE FUNCTION dbo.fn_Companies_HasCycle(@CompanyId UNIQUEIDENTIFIER, @NewParentId UNIQUEIDENTIFIER)
RETURNS BIT
AS
BEGIN
    -- Verifică dacă setarea NewParentId creează un ciclu
    -- Parcurge ierarhia în sus de la NewParentId și verifică dacă ajunge la CompanyId
    
    DECLARE @CurrentId UNIQUEIDENTIFIER = @NewParentId;
    DECLARE @MaxDepth INT = 10; -- Previne infinite loop
    DECLARE @Depth INT = 0;
    
    WHILE @CurrentId IS NOT NULL AND @Depth < @MaxDepth
    BEGIN
        IF @CurrentId = @CompanyId
            RETURN 1; -- Ciclu detectat!
        
        SELECT @CurrentId = ParentCompanyId 
        FROM Companies 
        WHERE Id = @CurrentId;
        
        SET @Depth = @Depth + 1;
    END
    
    RETURN 0; -- Fără ciclu
END;
GO

PRINT '✅ Funcție fn_Companies_HasCycle creată cu succes';
GO

-- =============================================
-- TRIGGER: Validare cicluri la INSERT/UPDATE
-- =============================================
IF OBJECT_ID('dbo.tr_Companies_PreventCycle', 'TR') IS NOT NULL
    DROP TRIGGER dbo.tr_Companies_PreventCycle;
GO

CREATE TRIGGER tr_Companies_PreventCycle
ON Companies
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    
    IF UPDATE(ParentCompanyId) OR EXISTS(SELECT 1 FROM inserted WHERE ParentCompanyId IS NOT NULL)
    BEGIN
        IF EXISTS (
            SELECT 1 FROM inserted i
            WHERE i.ParentCompanyId IS NOT NULL
            AND dbo.fn_Companies_HasCycle(i.Id, i.ParentCompanyId) = 1
        )
        BEGIN
            RAISERROR('Eroare: ParentCompanyId creează un ciclu în ierarhie!', 16, 1);
            ROLLBACK TRANSACTION;
            RETURN;
        END
    END
END;
GO

PRINT '✅ Trigger tr_Companies_PreventCycle creat cu succes';
GO

PRINT '=== Migrare 017_SocietateaProprie completă ===';
GO
