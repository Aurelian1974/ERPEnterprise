# 🏢 Soluție Tehnică: Pagina Societatea Proprie

**Data:** 11 Ianuarie 2026  
**Versiune:** 1.0  
**Status:** Propunere de arhitectură  
**Autor:** GitHub Copilot

---

## 📋 Cuprins

1. [Cerințe de Business](#1-cerințe-de-business)
2. [Arhitectura Bazei de Date](#2-arhitectura-bazei-de-date)
3. [Modele C# (Vertical Slices)](#3-modele-c-vertical-slices)
4. [Arhitectura UI/UX](#4-arhitectura-uiux)
5. [Implementare Detaliată](#5-implementare-detaliată)
6. [Plan de Implementare](#6-plan-de-implementare)

---

## 1. CERINȚE DE BUSINESS

### 1.1 Scenarii Suportate

| Scenariu | Descriere | Exemplu |
|----------|-----------|---------|
| **Companie Singulară** | O companie cu puncte de lucru și locații | SRL cu 3 magazine |
| **Grup de Companii** | Holding + subsidiare cu structuri mixte | Alfa Distribution |
| **WP+LOC** | Punct de lucru cu locații detaliate | Sediu cu depozit + birou |
| **WP-ONLY** | Punct de lucru simplu (fără locații) | Sucursală mică |
| **LOC-ONLY** | Locație fără punct de lucru ONRC | Teren, parcare, comodat |

### 1.2 Entități Principale

```
┌─────────────────────────────────────────────────────────────────┐
│                         GRUP (opțional)                         │
│                  (ex: ALFA DISTRIBUTION GROUP)                  │
└─────────────────────────────────────────────────────────────────┘
                                │
          ┌─────────────────────┼─────────────────────┐
          ▼                     ▼                     ▼
┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐
│    COMPANIE     │   │    COMPANIE     │   │    COMPANIE     │
│   (Juridică)    │   │   (Juridică)    │   │   (Juridică)    │
│  ALFA HOLDING   │   │  ALFA LOGISTICS │   │  ALFA RETAIL    │
└────────┬────────┘   └────────┬────────┘   └────────┬────────┘
         │                     │                     │
    ┌────┴────┐          ┌─────┼─────┐          ┌────┴────┐
    ▼         ▼          ▼     ▼     ▼          ▼         ▼
┌───────┐ ┌───────┐ ┌───────┐ ┌──────┐     ┌───────┐ ┌───────┐
│  WP   │ │  LOC  │ │  WP   │ │ LOC  │     │  WP   │ │  WP   │
│ Sediu │ │ Teren │ │Brașov │ │Ghimb.│     │Magazin│ │ Mall  │
└───┬───┘ └───────┘ └───┬───┘ └──────┘     └───┬───┘ └───────┘
    │                   │                      │
    │            ┌──────┼──────┐          ┌────┴────┐
    ▼            ▼      ▼      ▼          ▼         ▼
┌───────┐   ┌───────┐┌──────┐┌──────┐ ┌───────┐ ┌───────┐
│  LOC  │   │  LOC  ││ LOC  ││ LOC  │ │  LOC  │ │  LOC  │
│ Birou │   │Depozit││Birou ││Show. │ │ Show. │ │Depozit│
└───────┘   └───────┘└──────┘└──────┘ └───────┘ └───────┘
```

### 1.3 Atribute per Entitate

| Entitate | Atribute Cheie |
|----------|----------------|
| **Grup** | Nume, Descriere, LogoUrl, Website |
| **Companie** | CUI, Denumire, RegCom, SediuSocial, CapitalSocial, TipCompanie (Holding/Subsidiary), ProcentDetinere |
| **Punct Lucru** | Cod ONRC, Denumire, Adresa, Tip (Sediu/Sucursală/Agenție), EsteSediuSocial |
| **Locație** | Cod Intern, Denumire, Adresa, Tip (Depozit/Magazin/Birou/Teren/Service), Flags (Stoc/Vânzare/Achiziție) |

---

## 2. ARHITECTURA BAZEI DE DATE

### 2.1 Diagrama ERD

```
┌──────────────────────────────────────────────────────────────────────────┐
│                              COMPANY GROUPS                              │
├──────────────────────────────────────────────────────────────────────────┤
│ Id (PK, GUID)                                                            │
│ Denumire NVARCHAR(200) NOT NULL                                          │
│ Descriere NVARCHAR(500)                                                  │
│ LogoUrl NVARCHAR(500)                                                    │
│ Website NVARCHAR(200)                                                    │
│ IsActive BIT                                                             │
│ CreatedAt, UpdatedAt, CreatedBy, UpdatedBy                               │
└──────────────────────────────────────────────────────────────────────────┘
                                      │
                                      │ 1:N
                                      ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                               COMPANIES                                  │
│                    ❌ FĂRĂ Sediu Social Duplicat!                         │
├──────────────────────────────────────────────────────────────────────────┤
│ Id (PK, GUID)                                                            │
│ GroupId (FK → CompanyGroups, NULLABLE)                                   │
│ ParentCompanyId (FK → Companies, NULLABLE) + CK_NoSelfReference          │
│ CUI NOT NULL UNIQUE, RegCom, Denumire, DenumireScurta                    │
│ TipCompanie (0=Independent, 1=Holding, 2=Subsidiary)                     │
│ ProcentDetinere, CapitalSocial                                           │
│ Telefon, Email, Website, LogoUrl ─── Contact la nivel companie           │
│ IsPrincipal, IsActive, SortOrder                                         │
│                                                                          │
│ ⚠️ Sediu Social = VIEW vw_CompanyWithSediuSocial                         │
│    (JOIN cu WorkPlaces WHERE EsteSediuSocial = 1)                        │
└──────────────────────────────────────────────────────────────────────────┘
                                      │
              ┌───────────────────────┴───────────────────────┐
              │ 1:N                                           │ 1:N
              ▼                                               ▼
┌─────────────────────────────────┐         ┌─────────────────────────────────┐
│        WORK PLACES              │         │          LOCATIONS              │
│   ✅ Cu RegimJuridic!           │         │    (Locații fără WP - LOC-ONLY) │
├─────────────────────────────────┤         ├─────────────────────────────────┤
│ Id (PK, GUID)                   │         │ Id (PK, GUID)                   │
│ CompanyId (FK) NOT NULL         │         │ CompanyId (FK) NOT NULL         │
│ CodONRC NVARCHAR(50)            │         │ WorkPlaceId (FK) NULLABLE ◄─────┼── NULL = LOC-ONLY
│ Denumire NOT NULL               │         │ CodIntern NOT NULL              │
│ TipPunctLucru (Sediu/Sucurs.)   │         │ Denumire NOT NULL               │
│ ✅ RegimJuridic (Prop/Închir.)  │         │ TipLocatie (Depozit/Magazin...) │
│ Adresa, Localitate, Judet       │         │ Adresa, Localitate, Judet       │
│ CodPostal, Tara                 │         │ RegimJuridic (Prop/Închir...)   │
│ Telefon, Email                  │         │ HasStock, CanSell, CanPurchase  │
│ ✅ EsteSediuSocial BIT          │         │ CanManufacture, Suprafata       │
│   (UNIQUE per Company!)         │         │ Descriere                       │
│ DataInregistrare, DataRadiere   │         │ IsActive, SortOrder             │
│ IsActive, SortOrder             │         │ CreatedAt, UpdatedAt            │
└─────────────────────────────────┘         └─────────────────────────────────┘
              │                             
              │ 1:N                         
              ▼                             
┌─────────────────────────────────┐
│     LOCATIONS (sub WP)          │
│  (WorkPlaceId NOT NULL)         │
└─────────────────────────────────┘
```

### 2.2 Decizii de Design Revizuite

#### ⚠️ Problemă: Sediu Social Duplicat

**Problema originală:**
- `Companies.SediuSocialAdresa/Localitate/Judet` duplică datele din `WorkPlace` cu `EsteSediuSocial = true`
- La mutarea sediului → trebuie sincronizat în 2 locuri

**Soluție adoptată:** 
- ❌ Eliminăm câmpurile de sediu din `Companies`
- ✅ Sediul social = `WorkPlace` cu `EsteSediuSocial = true`
- ✅ View SQL `vw_CompanyWithSediuSocial` pentru acces rapid

#### ⚠️ Problemă: RegimJuridic lipsă pe WorkPlace

**Soluție:** Adăugăm `RegimJuridic` și pe `WorkPlaces` (sediu poate fi închiriat)

#### ⚠️ Problemă: Cicluri pe ParentCompanyId

**Soluție:**
- CHECK constraint în SQL (nivel 1)
- Validare recursivă în Service (nivele multiple)

---

### 2.3 Script SQL (Revizuit)

```sql
-- =============================================
-- Tabele pentru Societatea Proprie - v2.0
-- =============================================

-- 1. GRUPURI DE COMPANII
IF OBJECT_ID('dbo.CompanyGroups', 'U') IS NULL
CREATE TABLE [dbo].[CompanyGroups] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [Denumire] NVARCHAR(200) NOT NULL,
    [Descriere] NVARCHAR(500) NULL,
    [LogoUrl] NVARCHAR(500) NULL,
    [Website] NVARCHAR(200) NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIME2 NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL
);
GO

-- 2. COMPANII (fără duplicare sediu social!)
IF OBJECT_ID('dbo.Companies', 'U') IS NULL
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
    
    -- ❌ ELIMINAT: SediuSocialAdresa/Localitate/Judet
    -- ✅ Sediul social = WorkPlace cu EsteSediuSocial = true
    -- ✅ Acces via View vw_CompanyWithSediuSocial
    
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
    
    -- ⚠️ Previne self-reference directă (A -> A)
    CONSTRAINT [CK_Companies_NoSelfReference] CHECK ([ParentCompanyId] <> [Id])
);
GO

-- 3. PUNCTE DE LUCRU (cu RegimJuridic!)
IF OBJECT_ID('dbo.WorkPlaces', 'U') IS NULL
CREATE TABLE [dbo].[WorkPlaces] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [CompanyId] UNIQUEIDENTIFIER NOT NULL,
    
    -- Identificare ONRC
    [CodONRC] NVARCHAR(50) NULL, -- Poate fi NULL pentru puncte neînregistrate
    [Denumire] NVARCHAR(200) NOT NULL,
    
    -- Tip punct de lucru
    [TipPunctLucru] TINYINT NOT NULL DEFAULT 0, -- 0=SediuSocial, 1=Sucursala, 2=Agentie, 3=PunctLucru
    
    -- ✅ ADĂUGAT: Regim juridic (sediul poate fi închiriat!)
    [RegimJuridic] TINYINT NOT NULL DEFAULT 0,
    -- 0=Proprietate, 1=Inchiriere, 2=Comodat, 3=Leasing
    
    -- Adresa
    [Adresa] NVARCHAR(500) NOT NULL,
    [Localitate] NVARCHAR(100) NOT NULL,
    [Judet] NVARCHAR(50) NOT NULL,
    [CodPostal] NVARCHAR(10) NULL,
    [Tara] NVARCHAR(50) NOT NULL DEFAULT 'România',
    
    -- Contact
    [Telefon] NVARCHAR(20) NULL,
    [Email] NVARCHAR(100) NULL,
    
    -- Flags
    [EsteSediuSocial] BIT NOT NULL DEFAULT 0, -- ✅ SURSA UNICĂ pentru adresa sediului social!
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
GO

-- ⚠️ Constraint: O singură locație ca sediu social per companie
-- (implementat via UNIQUE filtered index)
CREATE UNIQUE NONCLUSTERED INDEX [UQ_WorkPlaces_SediuSocial_PerCompany]
ON [dbo].[WorkPlaces] ([CompanyId])
WHERE [EsteSediuSocial] = 1 AND [IsActive] = 1;
GO

-- 4. LOCAȚII
IF OBJECT_ID('dbo.Locations', 'U') IS NULL
CREATE TABLE [dbo].[Locations] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [CompanyId] UNIQUEIDENTIFIER NOT NULL FOREIGN KEY REFERENCES [dbo].[Companies]([Id]),
    [WorkPlaceId] UNIQUEIDENTIFIER NULL FOREIGN KEY REFERENCES [dbo].[WorkPlaces]([Id]), -- NULL = LOC-ONLY
    
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
    
    CONSTRAINT [UQ_Locations_CodIntern_Company] UNIQUE ([CompanyId], [CodIntern])
);
GO

-- =============================================
-- INDEXURI COMPLETE (SQL Server NU creează automat pe FK-uri!)
-- =============================================

-- CompanyGroups
CREATE INDEX IX_CompanyGroups_IsActive ON CompanyGroups(IsActive) INCLUDE (Denumire);
GO

-- Companies
CREATE INDEX IX_Companies_GroupId ON Companies(GroupId) WHERE GroupId IS NOT NULL;
CREATE INDEX IX_Companies_ParentCompanyId ON Companies(ParentCompanyId) WHERE ParentCompanyId IS NOT NULL;
CREATE UNIQUE NONCLUSTERED INDEX IX_Companies_CUI ON Companies(CUI); -- Căutare frecventă
CREATE INDEX IX_Companies_IsActive ON Companies(IsActive) INCLUDE (Denumire, CUI, TipCompanie);
CREATE INDEX IX_Companies_TipCompanie ON Companies(TipCompanie) WHERE IsActive = 1;
GO

-- WorkPlaces
CREATE INDEX IX_WorkPlaces_CompanyId ON WorkPlaces(CompanyId);
CREATE INDEX IX_WorkPlaces_CodONRC ON WorkPlaces(CodONRC) WHERE CodONRC IS NOT NULL;
CREATE INDEX IX_WorkPlaces_EsteSediuSocial ON WorkPlaces(EsteSediuSocial, CompanyId) WHERE IsActive = 1;
CREATE INDEX IX_WorkPlaces_IsActive ON WorkPlaces(IsActive) INCLUDE (Denumire, TipPunctLucru);
GO

-- Locations
CREATE INDEX IX_Locations_CompanyId ON Locations(CompanyId);
CREATE INDEX IX_Locations_WorkPlaceId ON Locations(WorkPlaceId) WHERE WorkPlaceId IS NOT NULL;
CREATE INDEX IX_Locations_CodIntern ON Locations(CodIntern, CompanyId); -- Căutare frecventă
CREATE INDEX IX_Locations_TipLocatie ON Locations(TipLocatie) WHERE IsActive = 1;
CREATE INDEX IX_Locations_Capabilities ON Locations(HasStock, CanSell, CanPurchase) WHERE IsActive = 1;
GO

-- =============================================
-- VIEW: Company cu Sediu Social (Single Source of Truth)
-- =============================================
CREATE OR ALTER VIEW vw_CompanyWithSediuSocial AS
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
    -- Sediu Social din WorkPlace (Single Source of Truth!)
    wp.Id AS SediuSocialId,
    wp.Adresa AS SediuSocialAdresa,
    wp.Localitate AS SediuSocialLocalitate,
    wp.Judet AS SediuSocialJudet,
    wp.CodPostal AS SediuSocialCodPostal,
    wp.Tara AS SediuSocialTara,
    wp.RegimJuridic AS SediuSocialRegimJuridic,
    wp.CodONRC AS SediuSocialCodONRC
FROM Companies c
LEFT JOIN WorkPlaces wp ON wp.CompanyId = c.Id 
    AND wp.EsteSediuSocial = 1 
    AND wp.IsActive = 1;
GO

-- =============================================
-- FUNCȚIE: Validare cicluri ParentCompanyId
-- =============================================
CREATE OR ALTER FUNCTION fn_Companies_HasCycle(@CompanyId UNIQUEIDENTIFIER, @NewParentId UNIQUEIDENTIFIER)
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

-- Trigger pentru validare cicluri la UPDATE
CREATE OR ALTER TRIGGER tr_Companies_PreventCycle
ON Companies
AFTER INSERT, UPDATE
AS
BEGIN
    IF UPDATE(ParentCompanyId) OR EXISTS(SELECT 1 FROM inserted)
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

PRINT '✅ Indexuri, View și Trigger pentru cicluri create cu succes!';
```

### 2.3 Vizualizări (Views) pentru UI

```sql
-- View pentru TreeView complet
CREATE VIEW vw_OrganizationTree AS
SELECT 
    'GROUP' AS NodeType,
    g.Id AS NodeId,
    NULL AS ParentId,
    g.Denumire AS Denumire,
    NULL AS CodONRC,
    NULL AS TipNode,
    0 AS Level,
    g.IsActive,
    g.SortOrder
FROM CompanyGroups g
WHERE g.IsActive = 1

UNION ALL

SELECT 
    'COMPANY' AS NodeType,
    c.Id AS NodeId,
    COALESCE(c.GroupId, c.ParentCompanyId) AS ParentId,
    c.Denumire + ' (' + c.CUI + ')' AS Denumire,
    c.RegCom AS CodONRC,
    CASE c.TipCompanie 
        WHEN 0 THEN 'Independent'
        WHEN 1 THEN 'Holding'
        WHEN 2 THEN 'Subsidiary'
    END AS TipNode,
    1 AS Level,
    c.IsActive,
    c.SortOrder
FROM Companies c
WHERE c.IsActive = 1

UNION ALL

SELECT 
    'WORKPLACE' AS NodeType,
    wp.Id AS NodeId,
    wp.CompanyId AS ParentId,
    wp.Denumire AS Denumire,
    wp.CodONRC,
    CASE wp.TipPunctLucru
        WHEN 0 THEN 'Sediu Social'
        WHEN 1 THEN 'Sucursală'
        WHEN 2 THEN 'Agenție'
        WHEN 3 THEN 'Punct Lucru'
    END AS TipNode,
    2 AS Level,
    wp.IsActive,
    wp.SortOrder
FROM WorkPlaces wp
WHERE wp.IsActive = 1

UNION ALL

SELECT 
    'LOCATION' AS NodeType,
    l.Id AS NodeId,
    COALESCE(l.WorkPlaceId, l.CompanyId) AS ParentId,
    l.Denumire + 
        CASE WHEN l.HasStock = 1 THEN ' ✓Stoc' ELSE '' END +
        CASE WHEN l.CanSell = 1 THEN ' ✓Vânzare' ELSE '' END +
        CASE WHEN l.CanPurchase = 1 THEN ' ✓Achiziție' ELSE '' END
    AS Denumire,
    l.CodIntern AS CodONRC,
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
    CASE WHEN l.WorkPlaceId IS NULL THEN 2 ELSE 3 END AS Level,
    l.IsActive,
    l.SortOrder
FROM Locations l
WHERE l.IsActive = 1;
GO
```

---

## 3. MODELE C# (VERTICAL SLICES)

### 3.1 Structura Foldere

```
Features/
└── Administrare/
    └── SocietateaProprie/
        ├── Models/
        │   ├── CompanyGroup.cs
        │   ├── Company.cs
        │   ├── WorkPlace.cs
        │   ├── Location.cs
        │   ├── OrganizationTreeNode.cs
        │   └── Enums/
        │       ├── TipCompanie.cs
        │       ├── TipPunctLucru.cs
        │       ├── TipLocatie.cs
        │       └── RegimJuridic.cs
        ├── Repositories/
        │   ├── ICompanyRepository.cs
        │   ├── CompanyRepository.cs
        │   ├── IWorkPlaceRepository.cs
        │   ├── WorkPlaceRepository.cs
        │   ├── ILocationRepository.cs
        │   └── LocationRepository.cs
        ├── Services/
        │   ├── IOrganizationService.cs
        │   └── OrganizationService.cs
        └── OrganizationAdaptor.cs

Components/
└── Pages/
    └── Administrare/
        ├── SocietateaProprie.razor
        ├── SocietateaProprie.razor.cs
        └── SocietateaProprie.razor.css
```

### 3.2 Modele

```csharp
// Features/Administrare/SocietateaProprie/Models/Enums/TipCompanie.cs
public enum TipCompanie
{
    Independent = 0,
    Holding = 1,
    Subsidiary = 2
}

// Features/Administrare/SocietateaProprie/Models/Enums/TipPunctLucru.cs
public enum TipPunctLucru
{
    SediuSocial = 0,
    Sucursala = 1,
    Agentie = 2,
    PunctLucru = 3
}

// Features/Administrare/SocietateaProprie/Models/Enums/TipLocatie.cs
public enum TipLocatie
{
    Depozit = 0,
    Magazin = 1,
    Birou = 2,
    Showroom = 3,
    Teren = 4,
    Service = 5,
    Parcare = 6,
    Altele = 7
}

// Features/Administrare/SocietateaProprie/Models/Enums/RegimJuridic.cs
public enum RegimJuridic
{
    Proprietate = 0,
    Inchiriere = 1,
    Comodat = 2,
    Leasing = 3
}
```

```csharp
// Features/Administrare/SocietateaProprie/Models/Company.cs
public class Company
{
    public Guid Id { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? ParentCompanyId { get; set; }
    
    [Required, StringLength(20)]
    public string CUI { get; set; } = string.Empty;
    
    [Required, StringLength(200)]
    public string Denumire { get; set; } = string.Empty;
    
    [StringLength(50)]
    public string? DenumireScurta { get; set; }
    
    [StringLength(50)]
    public string? RegCom { get; set; }
    
    public TipCompanie TipCompanie { get; set; } = TipCompanie.Independent;
    
    [Range(0, 100)]
    public decimal? ProcentDetinere { get; set; }
    
    public decimal? CapitalSocial { get; set; }
    
    // ❌ ELIMINAT: SediuSocialAdresa/Localitate/Judet
    // ✅ Sediu Social = VIEW vw_CompanyWithSediuSocial
    
    // Contact la nivel companie (nu sediu!)
    public string? Telefon { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? LogoUrl { get; set; }
    
    public bool IsPrincipal { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    
    // Navigation (pentru UI - populate din View)
    public string? GroupName { get; set; }
    public string? ParentCompanyName { get; set; }
    public int WorkPlaceCount { get; set; }
    public int LocationCount { get; set; }
    
    // ✅ Sediu Social din View (read-only)
    public Guid? SediuSocialId { get; set; }
    public string? SediuSocialAdresa { get; set; }
    public string? SediuSocialLocalitate { get; set; }
    public string? SediuSocialJudet { get; set; }
    public RegimJuridic? SediuSocialRegimJuridic { get; set; }
}

// Features/Administrare/SocietateaProprie/Models/WorkPlace.cs
public class WorkPlace
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    
    [StringLength(50)]
    public string? CodONRC { get; set; }
    
    [Required, StringLength(200)]
    public string Denumire { get; set; } = string.Empty;
    
    public TipPunctLucru TipPunctLucru { get; set; } = TipPunctLucru.SediuSocial;
    
    // ✅ ADĂUGAT: Regim juridic și pe WorkPlace!
    public RegimJuridic RegimJuridic { get; set; } = RegimJuridic.Proprietate;
    
    [Required, StringLength(500)]
    public string Adresa { get; set; } = string.Empty;
    
    [Required, StringLength(100)]
    public string Localitate { get; set; } = string.Empty;
    
    [Required, StringLength(50)]
    public string Judet { get; set; } = string.Empty;
    
    [StringLength(10)]
    public string? CodPostal { get; set; }
    
    public string Tara { get; set; } = "România";
    
    public string? Telefon { get; set; }
    public string? Email { get; set; }
    
    // ✅ Sursa unică pentru sediu social!
    public bool EsteSediuSocial { get; set; }
    public bool IsActive { get; set; } = true;
    
    public DateTime? DataInregistrare { get; set; }
    public DateTime? DataRadiere { get; set; }
    
    public int SortOrder { get; set; }
    
    // Navigation
    public string? CompanyName { get; set; }
    public int LocationCount { get; set; }
}
```

```csharp
// Features/Administrare/SocietateaProprie/Models/OrganizationTreeNode.cs
/// <summary>
/// Nod pentru reprezentare ierarhică în TreeView.
/// Poate fi Group, Company, WorkPlace sau Location.
/// </summary>
public class OrganizationTreeNode
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string NodeType { get; set; } = string.Empty; // GROUP, COMPANY, WORKPLACE, LOCATION
    public string Denumire { get; set; } = string.Empty;
    public string? Cod { get; set; } // CUI, CodONRC, CodIntern
    public string? TipNode { get; set; } // Holding, Sucursală, Depozit, etc.
    public int Level { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    
    // Flags pentru locații
    public bool HasStock { get; set; }
    public bool CanSell { get; set; }
    public bool CanPurchase { get; set; }
    
    // Pentru TreeView binding
    public bool IsExpanded { get; set; } = true;
    public bool HasChildren { get; set; }
    public List<OrganizationTreeNode> Children { get; set; } = new();
    
    // Icon based on NodeType
    public string Icon => NodeType switch
    {
        "GROUP" => "bi-building",
        "COMPANY" => "bi-bank",
        "WORKPLACE" => "bi-geo-alt",
        "LOCATION" => GetLocationIcon(),
        _ => "bi-circle"
    };
    
    private string GetLocationIcon() => TipNode switch
    {
        "Depozit" => "bi-box-seam",
        "Magazin" => "bi-shop",
        "Birou" => "bi-briefcase",
        "Showroom" => "bi-display",
        "Teren" => "bi-tree",
        "Service" => "bi-wrench",
        "Parcare" => "bi-p-square",
        _ => "bi-pin-map"
    };
    
    // Badge color based on capabilities
    public string BadgeClass => HasStock || CanSell || CanPurchase 
        ? "bg-success" 
        : "bg-secondary";
}
```

---

## 4. ARHITECTURA UI/UX

### 4.1 Layout Propus: Master-Detail cu TreeView

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 🏢 Societatea Proprie                                    [+ Grup] [+ Companie] │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────────────────────┐  ┌──────────────────────────────────────────┐ │
│  │ 🔍 Caută...             │  │                                          │ │
│  ├─────────────────────────┤  │         DETALII ENTITATE SELECTATĂ       │ │
│  │                         │  │                                          │ │
│  │ ▼ 📁 ALFA DISTRIBUTION  │  │  ┌────────────────────────────────────┐  │ │
│  │   ▼ 🏛 ALFA HOLDING     │  │  │ 🏛 ALFA LOGISTICS SRL              │  │ │
│  │     └ 📍 Sediu Social   │  │  │                                    │  │ │
│  │   ▼ 🏛 ALFA LOGISTICS   │  │  │ CUI: RO12345678                    │  │ │
│  │     ▼ 📍 Sediu Brașov   │  │  │ Reg.Com: J08/123/2020              │  │ │
│  │       ├ 📦 Depozit      │  │  │ Tip: Subsidiary (100%)             │  │ │
│  │       ├ 📋 Birou        │  │  │                                    │  │ │
│  │       └ 🏪 Showroom     │  │  │ ─────────────────────────────────  │  │ │
│  │     ▼ 📍 Sucursala Buc. │  │  │                                    │  │ │
│  │       └ 📦 Depozit Buc. │  │  │ 📍 Puncte de lucru: 3              │  │ │
│  │     📍 Sucursala Cluj   │  │  │ 📌 Locații: 7                      │  │ │
│  │     🌳 Teren Ghimbav    │  │  │   ├ Cu stoc: 4                     │  │ │
│  │     🅿 Parcare TIR      │  │  │   ├ Vânzare: 2                     │  │ │
│  │   ▼ 🏛 ALFA RETAIL      │  │  │   └ Achiziție: 3                   │  │ │
│  │     ...                 │  │  │                                    │  │ │
│  │                         │  │  └────────────────────────────────────┘  │ │
│  │                         │  │                                          │ │
│  │                         │  │  [Editează] [Adaugă WP] [Adaugă Locație] │ │
│  │                         │  │  [Dezactivează] [Șterge]                 │ │
│  │                         │  │                                          │ │
│  └─────────────────────────┘  └──────────────────────────────────────────┘ │
│                                                                              │
│  ◀ 280px fix                  ▶ flex-grow                                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Alternative UI - Tabs + Grid

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 🏢 Societatea Proprie                                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────┬─────────────────┬───────────────┬───────────┐                  │
│  │ Grupuri │ Companii        │ Puncte Lucru  │ Locații   │                  │
│  └─────────┴─────────────────┴───────────────┴───────────┘                  │
│                                                                              │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                                                                       │   │
│  │  [SfDataGrid cu toate companiile]                                    │   │
│  │                                                                       │   │
│  │  • Grupare pe Grup                                                   │   │
│  │  • Filtrare pe Tip Companie                                          │   │
│  │  • Hierarhie Parent-Child vizibilă                                   │   │
│  │                                                                       │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.3 Recomandare: Hybrid Approach

**Soluția optimă:** TreeView + Detail Panel + Modal Dialogs

| Componentă | Tehnologie | Scop |
|------------|------------|------|
| **TreeView** | `SfTreeView` Syncfusion | Navigare ierarhie |
| **Detail Panel** | Custom Blazor component | Vizualizare detalii |
| **Edit Dialogs** | `SfDialog` per tip entitate | CRUD operations |
| **Quick Actions** | Context menu / Toolbar | Add child, Edit, Delete |

---

## 5. IMPLEMENTARE DETALIATĂ

### 5.1 Componenta TreeView cu Syncfusion

```html
<!-- SocietateaProprie.razor -->
@page "/administrare/societatea-proprie"
@attribute [Authorize]
@rendermode InteractiveServer

<PageTitle>Societatea Proprie</PageTitle>

<div class="organization-page">
    @* Page Header *@
    <div class="page-header">
        <div class="header-content">
            <h3 class="page-title">
                <i class="bi bi-building me-2"></i>Societatea Proprie
            </h3>
        </div>
        <div class="header-actions">
            <SfButton CssClass="e-primary" @onclick="AddGroup">
                <i class="bi bi-folder-plus me-1"></i>Grup Nou
            </SfButton>
            <SfButton CssClass="e-primary" @onclick="AddCompany">
                <i class="bi bi-bank me-1"></i>Companie Nouă
            </SfButton>
        </div>
    </div>

    <div class="organization-container">
        @* Left Panel - TreeView *@
        <div class="tree-panel">
            <div class="tree-search">
                <SfTextBox Placeholder="Caută..." 
                           @bind-Value="searchTerm"
                           Input="OnSearchInput">
                    <TextBoxEvents TValue="string" Input="OnSearchInput" />
                </SfTextBox>
            </div>
            
            <SfTreeView @ref="treeView"
                        TValue="OrganizationTreeNode"
                        ShowCheckBox="false"
                        AllowDragAndDrop="true"
                        AllowEditing="false"
                        ExpandOn="ExpandAction.Click"
                        CssClass="organization-tree">
                <TreeViewFieldsSettings TValue="OrganizationTreeNode"
                                        Id="Id"
                                        ParentID="ParentId"
                                        Text="Denumire"
                                        HasChildren="HasChildren"
                                        Expanded="IsExpanded"
                                        DataSource="@treeNodes">
                </TreeViewFieldsSettings>
                <TreeViewTemplates TValue="OrganizationTreeNode">
                    <NodeTemplate>
                        <div class="tree-node @GetNodeClass(context)">
                            <i class="@context.Icon me-2"></i>
                            <span class="node-text">@context.Denumire</span>
                            @if (context.NodeType == "LOCATION")
                            {
                                <span class="node-badges">
                                    @if (context.HasStock)
                                    {
                                        <span class="badge bg-info">Stoc</span>
                                    }
                                    @if (context.CanSell)
                                    {
                                        <span class="badge bg-success">Vânzare</span>
                                    }
                                    @if (context.CanPurchase)
                                    {
                                        <span class="badge bg-warning">Achiziție</span>
                                    }
                                </span>
                            }
                        </div>
                    </NodeTemplate>
                </TreeViewTemplates>
                <TreeViewEvents TValue="OrganizationTreeNode"
                                NodeSelected="OnNodeSelected"
                                NodeClicked="OnNodeClicked">
                </TreeViewEvents>
            </SfTreeView>
        </div>
        
        @* Right Panel - Details *@
        <div class="detail-panel">
            @if (selectedNode == null)
            {
                <div class="empty-state">
                    <i class="bi bi-cursor-fill"></i>
                    <p>Selectați un element din structura organizațională</p>
                </div>
            }
            else
            {
                <div class="detail-content">
                    @switch (selectedNode.NodeType)
                    {
                        case "GROUP":
                            <GroupDetailCard Group="@selectedGroup" 
                                             OnEdit="EditGroup"
                                             OnAddCompany="AddCompanyToGroup" />
                            break;
                        case "COMPANY":
                            <CompanyDetailCard Company="@selectedCompany"
                                               OnEdit="EditCompany"
                                               OnAddWorkPlace="AddWorkPlaceToCompany"
                                               OnAddLocation="AddLocationToCompany" />
                            break;
                        case "WORKPLACE":
                            <WorkPlaceDetailCard WorkPlace="@selectedWorkPlace"
                                                 OnEdit="EditWorkPlace"
                                                 OnAddLocation="AddLocationToWorkPlace" />
                            break;
                        case "LOCATION":
                            <LocationDetailCard Location="@selectedLocation"
                                                OnEdit="EditLocation" />
                            break;
                    }
                </div>
            }
        </div>
    </div>
</div>

@* Dialogs pentru CRUD - fiecare tip de entitate *@
<CompanyDialog @ref="companyDialog" 
               Groups="@groups"
               Companies="@companies"
               OnSaved="OnCompanySaved" />

<WorkPlaceDialog @ref="workPlaceDialog"
                 Companies="@companies"
                 OnSaved="OnWorkPlaceSaved" />

<LocationDialog @ref="locationDialog"
                Companies="@companies"
                WorkPlaces="@workPlaces"
                OnSaved="OnLocationSaved" />
```

### 5.2 Componente Detail Cards

```csharp
// Components/Shared/Organization/CompanyDetailCard.razor
<div class="detail-card company-card">
    <div class="card-header">
        <div class="entity-icon">
            <i class="bi bi-bank"></i>
        </div>
        <div class="entity-info">
            <h4>@Company.Denumire</h4>
            <span class="entity-type">
                @Company.TipCompanie.ToString()
                @if (Company.ProcentDetinere.HasValue)
                {
                    <span class="ownership">(@Company.ProcentDetinere%)</span>
                }
            </span>
        </div>
    </div>
    
    <div class="card-body">
        <div class="info-grid">
            <div class="info-item">
                <label>CUI</label>
                <span>@Company.CUI</span>
            </div>
            <div class="info-item">
                <label>Reg. Com.</label>
                <span>@(Company.RegCom ?? "—")</span>
            </div>
            <div class="info-item full-width">
                <label>Sediu Social</label>
                <span>@FormatAddress(Company)</span>
            </div>
            @if (Company.CapitalSocial.HasValue)
            {
                <div class="info-item">
                    <label>Capital Social</label>
                    <span>@Company.CapitalSocial?.ToString("N2") RON</span>
                </div>
            }
        </div>
        
        <div class="stats-grid">
            <div class="stat-item">
                <i class="bi bi-geo-alt"></i>
                <span class="stat-value">@Company.WorkPlaceCount</span>
                <span class="stat-label">Puncte de lucru</span>
            </div>
            <div class="stat-item">
                <i class="bi bi-pin-map"></i>
                <span class="stat-value">@Company.LocationCount</span>
                <span class="stat-label">Locații</span>
            </div>
        </div>
    </div>
    
    <div class="card-actions">
        <SfButton CssClass="e-outline" @onclick="OnEdit">
            <i class="bi bi-pencil me-1"></i>Editează
        </SfButton>
        <SfButton CssClass="e-outline" @onclick="OnAddWorkPlace">
            <i class="bi bi-geo-alt-fill me-1"></i>Adaugă Punct Lucru
        </SfButton>
        <SfButton CssClass="e-outline" @onclick="OnAddLocation">
            <i class="bi bi-pin-map-fill me-1"></i>Adaugă Locație
        </SfButton>
    </div>
</div>
```

### 5.3 Context Menu pentru Quick Actions

```csharp
// În TreeView - adaugă context menu
<SfContextMenu @ref="contextMenu" TValue="MenuItem">
    <MenuItems>
        <MenuItem Text="Editează" IconCss="bi bi-pencil" Id="edit"></MenuItem>
        <MenuItem Separator="true"></MenuItem>
        <MenuItem Text="Adaugă Companie" IconCss="bi bi-bank" Id="addCompany" 
                  Disabled="@(!CanAddCompany())"></MenuItem>
        <MenuItem Text="Adaugă Punct Lucru" IconCss="bi bi-geo-alt" Id="addWP"
                  Disabled="@(!CanAddWorkPlace())"></MenuItem>
        <MenuItem Text="Adaugă Locație" IconCss="bi bi-pin-map" Id="addLoc"
                  Disabled="@(!CanAddLocation())"></MenuItem>
        <MenuItem Separator="true"></MenuItem>
        <MenuItem Text="Dezactivează" IconCss="bi bi-toggle-off" Id="deactivate"></MenuItem>
        <MenuItem Text="Șterge" IconCss="bi bi-trash text-danger" Id="delete"></MenuItem>
    </MenuItems>
    <MenuEvents TValue="MenuItem" ItemSelected="OnContextMenuSelect"></MenuEvents>
</SfContextMenu>
```

---

## 6. PLAN DE IMPLEMENTARE

### 6.1 Faze de Implementare

| Faza | Descriere | Efort | Dependențe |
|------|-----------|-------|------------|
| **Faza 1** | Baza de date + Modele + Repository | 8h | - |
| **Faza 2** | Service + Stored Procedures | 6h | Faza 1 |
| **Faza 3** | UI TreeView + Detail Panel | 12h | Faza 2 |
| **Faza 4** | Dialogs CRUD (4 tipuri) | 16h | Faza 3 |
| **Faza 5** | Validări + Business Rules | 6h | Faza 4 |
| **Faza 6** | Testing + Polish | 8h | Faza 5 |

**Total estimat:** ~56 ore (7 zile)

### 6.2 Priorități MVP

Pentru **MVP (Minimum Viable Product)**:

1. ✅ Tabel `Companies` + CRUD complet
2. ✅ Tabel `WorkPlaces` + CRUD complet
3. ✅ Tabel `Locations` + CRUD complet
4. ✅ TreeView de bază (fără drag & drop)
5. ✅ Detail cards simple
6. ⬜ Grupuri (poate fi adăugat ulterior)

**Efort MVP:** ~35 ore (4-5 zile)

### 6.3 Riscuri și Mitigări

| Risc | Impact | Mitigare |
|------|--------|----------|
| Complexitate ierarhie | Mare | Folosește View SQL pentru flatten |
| Performance TreeView 1000+ noduri | Mediu | Lazy loading pe nivele |
| Validări cross-entity | Mare | Validare server-side în Service |
| Drag & drop între companii | Mic | Dezactivează în MVP |

---

## 7. DECIZII ARHITECTURALE

### 7.1 De ce TreeView în loc de Grid-uri separate?

| Aspect | TreeView | Grid-uri Separate |
|--------|----------|-------------------|
| **Vizualizare ierarhie** | ✅ Naturală | ❌ Tab-uri, pierdere context |
| **Navigare** | ✅ Click simplu | ❌ Multiple clicks |
| **Adăugare copii** | ✅ Context menu | ❌ Select parent din dropdown |
| **Drag & drop reordering** | ✅ Suportat | ❌ Nu aplicabil |
| **Performance** | ⚠️ Lazy load necesar | ✅ Paginare server-side |
| **Mobile** | ⚠️ Mai puțin prietenos | ✅ Responsive grids |

**Concluzie:** TreeView pentru desktop, consider Grid pentru mobile view.

### 7.2 De ce `WorkPlaceId` nullable în `Locations`?

Permite scenariul **LOC-ONLY** (locații fără punct de lucru ONRC):
- Terenuri
- Parcări
- Spații în comodat neînregistrate

Alternative considerate:
1. ❌ Punct de lucru "virtual" - adaugă complexitate
2. ❌ Tabel separat `StandaloneLocations` - duplicare logică
3. ✅ `WorkPlaceId NULL` - simplu, flexibil

### 7.3 De ce nu am folosit Self-Referencing pentru toată ierarhia?

Am considerat un singur tabel `OrganizationUnits` cu `ParentId` self-referencing, dar:

**Dezavantaje:**
- Tipuri diferite de date (CUI pentru companii, CodONRC pentru WP)
- Validări diferite per tip
- Flags specifice locațiilor (HasStock, CanSell)
- Queries complexe pentru raportări

**Decizie:** Tabele separate cu View pentru TreeView.

---

## 8. EXTENSIBILITATE

### 8.1 Funcționalități Viitoare

| Feature | Complexitate | Prioritate |
|---------|--------------|------------|
| Import din ANAF API (date companie) | Medie | 🟡 |
| Export structură în Excel | Mică | 🟢 |
| Historicul modificărilor | Mare | 🔴 |
| Multi-tenant (mai multe grupuri) | Mare | 🔴 |
| Integrare cu module ERP (Stocuri, Vânzări) | Mare | 🔴 |

### 8.2 Integrare cu Restul ERP

```csharp
// Exemplu: Selecție locație în documente
public class DocumentService
{
    public async Task<IEnumerable<Location>> GetLocationsForSale(Guid companyId)
    {
        // Returnează doar locațiile cu CanSell = true
        return await _locationRepo.GetByCompanyAsync(companyId, 
            filter: l => l.CanSell && l.IsActive);
    }
    
    public async Task<IEnumerable<Location>> GetLocationsWithStock(Guid companyId)
    {
        // Returnează doar locațiile cu HasStock = true
        return await _locationRepo.GetByCompanyAsync(companyId,
            filter: l => l.HasStock && l.IsActive);
    }
}
```

---

## ✅ CONCLUZII

### Soluția Recomandată

1. **4 tabele relaționate**: CompanyGroups, Companies, WorkPlaces, Locations
2. **View SQL** pentru flatten ierarhie în TreeView
3. **UI Master-Detail**: TreeView (stânga) + Detail Panel (dreapta)
4. **4 Dialog-uri** separate pentru CRUD pe fiecare tip
5. **Context Menu** pentru acțiuni rapide în tree

### Next Steps

1. 📋 Validare design cu stakeholders
2. 🗄️ Creare script SQL migrare
3. 🏗️ Implementare Repository + Service
4. 🎨 Implementare UI TreeView
5. 🧪 Testing E2E

---

**Document creat:** 11 Ianuarie 2026  
**Autor:** GitHub Copilot  
**Status:** ✅ DESIGN COMPLET - AȘTEPTARE APROBARE
