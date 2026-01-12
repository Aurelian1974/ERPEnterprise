# 🏢 User Organizational Access System
## Design Document - Ierarhie și Permisiuni Organizaționale

**Data:** 12 Ianuarie 2026  
**Versiune:** 2.0 (Dapper Edition)  
**Status:** ⬜ PENDING REVIEW  
**Autor:** GitHub Copilot + Claude  

---

## 📋 Sumar Executiv

Acest document definește arhitectura pentru sistemul de **acces organizațional granular** în ValyanERP, care permite:
- Acces la entități din orice nivel al ierarhiei (Grup → Societate → Punct Lucru → Locație)
- Moștenire automată a accesului către entitățile subordonate
- Excludere explicită (Deny) pentru cazuri speciale
- Acces granular (ex: doar 2 locații din puncte de lucru diferite)
- Filtrare automată a datelor bazată pe perimetrul vizibil al userului
- **Implementare cu Dapper** (fără Entity Framework)

---

## 🏗️ Ierarhia Organizațională (EXISTENTĂ)

```
┌─────────────────────────────────────────────────────────────────────┐
│ NIVEL 1: GRUP (CompanyGroups)                                       │
│ - Holding, grup de companii                                         │
│ - Ex: "UTI GRUP"                                                    │
├─────────────────────────────────────────────────────────────────────┤
│ NIVEL 2: SOCIETATE (Companies)                                      │
│ - Entitate juridică cu CUI                                          │
│ - Ex: "UTI SYSTEMS SA - RO16326010"                                 │
├─────────────────────────────────────────────────────────────────────┤
│ NIVEL 3: PUNCT DE LUCRU (WorkPlaces)                                │
│ - Filială, sediu, sucursală                                         │
│ - Ex: "Sediu Central", "Filiala Cluj"                               │
├─────────────────────────────────────────────────────────────────────┤
│ NIVEL 4: LOCAȚIE (Locations)                                        │
│ - Spațiu fizic specific                                             │
│ - Ex: "Depozit Principal", "Showroom", "Service Auto"               │
└─────────────────────────────────────────────────────────────────────┘
```

### Tabele Existente (din 017_SocietateaProprie.sql):

| Tabel | Descriere | Parent FK |
|-------|-----------|-----------|
| `CompanyGroups` | Grupuri de companii | - |
| `Companies` | Societăți/Companii | `GroupId`, `ParentCompanyId` |
| `WorkPlaces` | Puncte de lucru | `CompanyId` |
| `Locations` | Locații | `CompanyId`, `WorkPlaceId` |

---

## 🎯 Cerințe Funcționale

### R1: Acces Multi-Nivel
Un utilizator poate avea acces la **una sau mai multe entități** din **orice nivel**:
- ✅ Acces la un Grup → vede TOATE companiile, punctele de lucru, locațiile din acel grup
- ✅ Acces la o Societate → vede TOATE punctele de lucru și locațiile din ea
- ✅ Acces la un Punct de Lucru → vede TOATE locațiile din acel punct
- ✅ Acces la o Locație → vede DOAR acea locație

### R2: Moștenire Implicită
Accesul la un nivel superior **implică automat** acces la toate entitățile subordonate.

### R3: Excludere Explicită (Deny)
Un user poate avea acces la un nivel superior DAR să fie **exclus explicit** de la anumite entități subordonate:
- Ex: Acces la Company X, DAR exclus de la Locația Y din Company X

### R4: Acces Granular
Un user poate avea acces **doar la entități specifice**, fără moștenire:
- Ex: Acces doar la Locația X din Punctul A și Locația Y din Punctul B
- NU are acces la celelalte locații din aceste puncte

### R5: Perimetru la Autentificare
La login, sistemul determină **perimetrul vizibil** al userului și îl cached în memorie.

### R6: Filtrare Automată
Toate query-urile pe date operaționale TREBUIE filtrate automat după perimetru.

### R7: Context de Lucru Activ
Userul poate selecta un **context activ** (companie/punct de lucru/locație) din perimetrul său pentru operațiuni curente.

---

## 💾 Design Baza de Date

### Tabel Principal: UserOrganizationalAccess

```sql
-- =============================================
-- USER ORGANIZATIONAL ACCESS
-- Un singur tabel pentru toate nivelurile
-- =============================================
CREATE TABLE [dbo].[UserOrganizationalAccess] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    
    -- Tipul entității la care are acces
    [EntityType] VARCHAR(20) NOT NULL, -- 'GROUP', 'COMPANY', 'WORKPLACE', 'LOCATION'
    [EntityId] UNIQUEIDENTIFIER NOT NULL,
    
    -- Tip acces (pentru extensibilitate viitoare)
    [AccessLevel] TINYINT NOT NULL DEFAULT 1, 
    -- 0 = NoAccess (explicit denied)
    -- 1 = Read
    -- 2 = Write
    -- 3 = Full (include delete)
    -- 4 = Admin (poate administra access-ul altora)
    
    -- Moștenire
    [InheritToChildren] BIT NOT NULL DEFAULT 1,
    -- TRUE = accesul se propagă la toate entitățile subordonate
    -- FALSE = acces DOAR la această entitate
    
    -- Valabilitate (opțional, pentru acces temporar)
    [ValidFrom] DATETIME2 NULL,
    [ValidTo] DATETIME2 NULL,
    
    -- Audit
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIME2 NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    [Notes] NVARCHAR(500) NULL,
    
    -- Constraints
    CONSTRAINT [FK_UOA_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UOA_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UOA_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [CK_UOA_EntityType] CHECK ([EntityType] IN ('GROUP', 'COMPANY', 'WORKPLACE', 'LOCATION')),
    CONSTRAINT [CK_UOA_AccessLevel] CHECK ([AccessLevel] BETWEEN 0 AND 4),
    CONSTRAINT [CK_UOA_ValidDates] CHECK ([ValidTo] IS NULL OR [ValidFrom] IS NULL OR [ValidTo] >= [ValidFrom])
);

-- Un user nu poate avea duplicate pentru aceeași entitate
CREATE UNIQUE INDEX [UQ_UOA_User_Entity] 
ON [UserOrganizationalAccess] ([UserId], [EntityType], [EntityId]) 
WHERE [IsActive] = 1;

-- Indexuri pentru query-uri rapide
CREATE INDEX [IX_UOA_UserId] ON [UserOrganizationalAccess] ([UserId]) INCLUDE ([EntityType], [EntityId], [AccessLevel], [InheritToChildren]);
CREATE INDEX [IX_UOA_EntityType_EntityId] ON [UserOrganizationalAccess] ([EntityType], [EntityId]) INCLUDE ([UserId], [AccessLevel]);
CREATE INDEX [IX_UOA_ValidDates] ON [UserOrganizationalAccess] ([ValidFrom], [ValidTo]) WHERE [IsActive] = 1;
```

### Tabel Cache pentru Performanță

```sql
-- =============================================
-- CACHE: Entități vizibile precalculate
-- Refresh la modificare permisiuni sau periodic
-- =============================================
CREATE TABLE [dbo].[UserVisibleEntitiesCache] (
    [Id] BIGINT IDENTITY(1,1) NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [CompanyId] UNIQUEIDENTIFIER NULL,
    [WorkPlaceId] UNIQUEIDENTIFIER NULL,
    [LocationId] UNIQUEIDENTIFIER NULL,
    [MaxAccessLevel] TINYINT NOT NULL,
    [CachedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    
    CONSTRAINT [PK_UserVisibleEntitiesCache] PRIMARY KEY CLUSTERED ([Id]),
    INDEX [IX_Cache_User] NONCLUSTERED ([UserId]),
    INDEX [IX_Cache_Location] NONCLUSTERED ([LocationId]) INCLUDE ([UserId], [MaxAccessLevel]),
    INDEX [IX_Cache_Company] NONCLUSTERED ([CompanyId]) INCLUDE ([UserId], [MaxAccessLevel])
);
```

### Tabel pentru Entity Sharing (Cross-Company)

```sql
-- =============================================
-- ENTITY SHARING
-- Pentru entități partajate între companii (ex: Parteneri comuni)
-- =============================================
CREATE TABLE [dbo].[EntitySharing] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    [EntityType] VARCHAR(50) NOT NULL,  -- 'Partner', 'Product', 'Service', etc.
    [EntityId] UNIQUEIDENTIFIER NOT NULL,
    [OwnerCompanyId] UNIQUEIDENTIFIER NOT NULL,  -- Compania care deține entitatea
    [SharedWithCompanyId] UNIQUEIDENTIFIER NOT NULL,  -- Compania cu care e partajată
    [AccessLevel] TINYINT NOT NULL DEFAULT 1,  -- 1=Read, 2=Write
    [SharedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [SharedBy] UNIQUEIDENTIFIER NULL,
    [IsActive] BIT NOT NULL DEFAULT 1,
    
    CONSTRAINT [FK_ES_OwnerCompany] FOREIGN KEY ([OwnerCompanyId]) REFERENCES [dbo].[Companies]([Id]),
    CONSTRAINT [FK_ES_SharedWithCompany] FOREIGN KEY ([SharedWithCompanyId]) REFERENCES [dbo].[Companies]([Id]),
    CONSTRAINT [FK_ES_SharedBy] FOREIGN KEY ([SharedBy]) REFERENCES [dbo].[Users]([Id]),
    
    INDEX [IX_ES_Entity] NONCLUSTERED ([EntityType], [EntityId]),
    INDEX [IX_ES_SharedWith] NONCLUSTERED ([SharedWithCompanyId], [EntityType])
);
```

### Tabel pentru Audit Acces Refuzat

```sql
-- =============================================
-- AUDIT: Log pentru încercări de acces refuzate
-- =============================================
CREATE TABLE [dbo].[AccessDeniedLog] (
    [Id] BIGINT IDENTITY(1,1) NOT NULL,
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    [AttemptedEntityType] VARCHAR(50) NOT NULL,
    [AttemptedEntityId] UNIQUEIDENTIFIER NOT NULL,
    [RequestedAction] VARCHAR(50) NOT NULL,  -- 'Read', 'Write', 'Delete'
    [DeniedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [IpAddress] VARCHAR(45) NULL,
    [UserAgent] NVARCHAR(500) NULL,
    [AdditionalInfo] NVARCHAR(MAX) NULL,
    
    CONSTRAINT [PK_AccessDeniedLog] PRIMARY KEY CLUSTERED ([Id]),
    INDEX [IX_ADL_User] NONCLUSTERED ([UserId], [DeniedAt] DESC),
    INDEX [IX_ADL_Entity] NONCLUSTERED ([AttemptedEntityType], [AttemptedEntityId])
);
```

### Type pentru Table-Valued Parameters

```sql
-- =============================================
-- TYPE: Lista de GUID-uri pentru parametri Dapper
-- =============================================
CREATE TYPE [dbo].[GuidList] AS TABLE (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
);
GO
```

---

## 🔍 Funcții SQL pentru Calculul Perimetrului

### Funcție: Locații Vizibile

```sql
-- =============================================
-- FUNCȚIE: Returnează toate Location IDs vizibile pentru un user
-- Include logica de DENY (AccessLevel = 0)
-- =============================================
CREATE OR ALTER FUNCTION [dbo].[fn_GetUserVisibleLocations](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    WITH AllowedLocations AS (
        -- 1. Locații cu acces DIRECT
        SELECT DISTINCT l.Id AS LocationId, uoa.AccessLevel
        FROM Locations l
        INNER JOIN UserOrganizationalAccess uoa 
            ON uoa.EntityType = 'LOCATION' AND uoa.EntityId = l.Id
        WHERE uoa.UserId = @UserId 
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel > 0
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
        
        UNION
        
        -- 2. Locații din WORKPLACE cu acces și inherit
        SELECT l.Id AS LocationId, uoa.AccessLevel
        FROM Locations l
        INNER JOIN UserOrganizationalAccess uoa 
            ON uoa.EntityType = 'WORKPLACE' AND uoa.EntityId = l.WorkPlaceId
        WHERE uoa.UserId = @UserId 
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel > 0
          AND uoa.InheritToChildren = 1
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
        
        UNION
        
        -- 3. Locații din COMPANY cu acces și inherit
        SELECT l.Id AS LocationId, uoa.AccessLevel
        FROM Locations l
        INNER JOIN UserOrganizationalAccess uoa 
            ON uoa.EntityType = 'COMPANY' AND uoa.EntityId = l.CompanyId
        WHERE uoa.UserId = @UserId 
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel > 0
          AND uoa.InheritToChildren = 1
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
        
        UNION
        
        -- 4. Locații din GRUP cu acces și inherit
        SELECT l.Id AS LocationId, uoa.AccessLevel
        FROM Locations l
        INNER JOIN Companies c ON l.CompanyId = c.Id
        INNER JOIN UserOrganizationalAccess uoa 
            ON uoa.EntityType = 'GROUP' AND uoa.EntityId = c.GroupId
        WHERE uoa.UserId = @UserId 
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel > 0
          AND uoa.InheritToChildren = 1
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    ),
    DeniedLocations AS (
        -- Locații explicit DENIED
        SELECT uoa.EntityId AS LocationId
        FROM UserOrganizationalAccess uoa
        WHERE uoa.UserId = @UserId 
          AND uoa.EntityType = 'LOCATION'
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel = 0  -- Explicit DENY
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    )
    SELECT al.LocationId, MAX(al.AccessLevel) AS AccessLevel
    FROM AllowedLocations al
    WHERE al.LocationId NOT IN (SELECT LocationId FROM DeniedLocations)
    GROUP BY al.LocationId
);
GO
```

### Funcție: WorkPlaces Vizibile

```sql
-- =============================================
-- FUNCȚIE: Returnează toate WorkPlace IDs vizibile pentru un user
-- =============================================
CREATE OR ALTER FUNCTION [dbo].[fn_GetUserVisibleWorkPlaces](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    WITH AllowedWorkPlaces AS (
        -- 1. WorkPlaces cu acces DIRECT
        SELECT DISTINCT wp.Id AS WorkPlaceId, uoa.AccessLevel
        FROM WorkPlaces wp
        INNER JOIN UserOrganizationalAccess uoa 
            ON uoa.EntityType = 'WORKPLACE' AND uoa.EntityId = wp.Id
        WHERE uoa.UserId = @UserId 
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel > 0
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
        
        UNION
        
        -- 2. WorkPlaces din COMPANY cu acces și inherit
        SELECT wp.Id AS WorkPlaceId, uoa.AccessLevel
        FROM WorkPlaces wp
        INNER JOIN UserOrganizationalAccess uoa 
            ON uoa.EntityType = 'COMPANY' AND uoa.EntityId = wp.CompanyId
        WHERE uoa.UserId = @UserId 
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel > 0
          AND uoa.InheritToChildren = 1
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
        
        UNION
        
        -- 3. WorkPlaces din GRUP cu acces și inherit
        SELECT wp.Id AS WorkPlaceId, uoa.AccessLevel
        FROM WorkPlaces wp
        INNER JOIN Companies c ON wp.CompanyId = c.Id
        INNER JOIN UserOrganizationalAccess uoa 
            ON uoa.EntityType = 'GROUP' AND uoa.EntityId = c.GroupId
        WHERE uoa.UserId = @UserId 
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel > 0
          AND uoa.InheritToChildren = 1
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    ),
    DeniedWorkPlaces AS (
        SELECT uoa.EntityId AS WorkPlaceId
        FROM UserOrganizationalAccess uoa
        WHERE uoa.UserId = @UserId 
          AND uoa.EntityType = 'WORKPLACE'
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel = 0
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    )
    SELECT awp.WorkPlaceId, MAX(awp.AccessLevel) AS AccessLevel
    FROM AllowedWorkPlaces awp
    WHERE awp.WorkPlaceId NOT IN (SELECT WorkPlaceId FROM DeniedWorkPlaces)
    GROUP BY awp.WorkPlaceId
);
GO
```

### Funcție: Companies Vizibile

```sql
-- =============================================
-- FUNCȚIE: Returnează toate Company IDs vizibile pentru un user
-- =============================================
CREATE OR ALTER FUNCTION [dbo].[fn_GetUserVisibleCompanies](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    WITH AllowedCompanies AS (
        -- 1. Companies cu acces DIRECT
        SELECT DISTINCT c.Id AS CompanyId, uoa.AccessLevel
        FROM Companies c
        INNER JOIN UserOrganizationalAccess uoa 
            ON uoa.EntityType = 'COMPANY' AND uoa.EntityId = c.Id
        WHERE uoa.UserId = @UserId 
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel > 0
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
        
        UNION
        
        -- 2. Companies din GRUP cu acces și inherit
        SELECT c.Id AS CompanyId, uoa.AccessLevel
        FROM Companies c
        INNER JOIN UserOrganizationalAccess uoa 
            ON uoa.EntityType = 'GROUP' AND uoa.EntityId = c.GroupId
        WHERE uoa.UserId = @UserId 
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel > 0
          AND uoa.InheritToChildren = 1
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    ),
    DeniedCompanies AS (
        SELECT uoa.EntityId AS CompanyId
        FROM UserOrganizationalAccess uoa
        WHERE uoa.UserId = @UserId 
          AND uoa.EntityType = 'COMPANY'
          AND uoa.IsActive = 1 
          AND uoa.AccessLevel = 0
          AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
          AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    )
    SELECT ac.CompanyId, MAX(ac.AccessLevel) AS AccessLevel
    FROM AllowedCompanies ac
    WHERE ac.CompanyId NOT IN (SELECT CompanyId FROM DeniedCompanies)
    GROUP BY ac.CompanyId
);
GO
```

### Funcție: Groups Vizibile

```sql
-- =============================================
-- FUNCȚIE: Returnează toate Group IDs vizibile pentru un user
-- =============================================
CREATE OR ALTER FUNCTION [dbo].[fn_GetUserVisibleGroups](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    SELECT DISTINCT cg.Id AS GroupId, MAX(uoa.AccessLevel) AS AccessLevel
    FROM CompanyGroups cg
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'GROUP' AND uoa.EntityId = cg.Id
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    GROUP BY cg.Id
);
GO
```

---

## 🔐 Views cu SESSION_CONTEXT pentru Filtrare Automată

### Setare SESSION_CONTEXT

```sql
-- =============================================
-- La fiecare conexiune, se setează UserId în SESSION_CONTEXT
-- Apelat din C# la deschiderea conexiunii
-- =============================================
-- EXEC sp_set_session_context @key = N'UserId', @value = @UserId;
-- EXEC sp_set_session_context @key = N'IsAdmin', @value = @IsAdmin;
```

### View Filtrat: Persoane

```sql
-- =============================================
-- VIEW: Persoane filtrate automat după perimetru
-- =============================================
CREATE OR ALTER VIEW [dbo].[vw_Persoane_Filtered]
AS
SELECT p.*
FROM Persoane p
WHERE p.IsActive = 1
AND (
    -- Admin vede tot
    CAST(SESSION_CONTEXT(N'IsAdmin') AS BIT) = 1
    OR
    -- Sau datele din companiile la care are acces
    p.OwnerCompanyId IN (
        SELECT CompanyId 
        FROM dbo.fn_GetUserVisibleCompanies(
            CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
        )
    )
    OR
    -- Sau datele pe care le-a creat
    p.CreatedBy = CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
);
GO
```

### View Filtrat: Partners

```sql
-- =============================================
-- VIEW: Partners filtrate automat (include sharing)
-- =============================================
CREATE OR ALTER VIEW [dbo].[vw_Partners_Filtered]
AS
SELECT p.*
FROM Partners p
WHERE p.IsActive = 1
AND (
    CAST(SESSION_CONTEXT(N'IsAdmin') AS BIT) = 1
    OR
    -- Parteneri din companiile proprii
    p.OwnerCompanyId IN (
        SELECT CompanyId 
        FROM dbo.fn_GetUserVisibleCompanies(
            CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
        )
    )
    OR
    -- Parteneri partajați cu companiile la care am acces
    EXISTS (
        SELECT 1 FROM EntitySharing es
        WHERE es.EntityType = 'Partner'
          AND es.EntityId = p.Id
          AND es.IsActive = 1
          AND es.SharedWithCompanyId IN (
              SELECT CompanyId 
              FROM dbo.fn_GetUserVisibleCompanies(
                  CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
              )
          )
    )
);
GO
```

---

## 📦 Stored Procedures CRUD

### SP: Refresh Cache pentru User

```sql
-- =============================================
-- PROCEDURE: Rebuild cache pentru un user specific
-- Apelat când se modifică permisiunile
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[sp_RefreshUserVisibleEntitiesCache]
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRANSACTION;
    
    -- Șterge cache-ul vechi
    DELETE FROM UserVisibleEntitiesCache WHERE UserId = @UserId;
    
    -- Rebuild cu toate entitățile vizibile
    INSERT INTO UserVisibleEntitiesCache (UserId, CompanyId, WorkPlaceId, LocationId, MaxAccessLevel, CachedAt)
    SELECT 
        @UserId,
        c.CompanyId,
        wp.WorkPlaceId,
        l.LocationId,
        COALESCE(l.AccessLevel, wp.AccessLevel, c.AccessLevel, 1) AS MaxAccessLevel,
        GETDATE()
    FROM dbo.fn_GetUserVisibleCompanies(@UserId) c
    LEFT JOIN dbo.fn_GetUserVisibleWorkPlaces(@UserId) wp 
        ON EXISTS (SELECT 1 FROM WorkPlaces w WHERE w.Id = wp.WorkPlaceId AND w.CompanyId = c.CompanyId)
    LEFT JOIN dbo.fn_GetUserVisibleLocations(@UserId) l 
        ON EXISTS (SELECT 1 FROM Locations loc WHERE loc.Id = l.LocationId AND loc.CompanyId = c.CompanyId);
    
    COMMIT TRANSACTION;
    
    SELECT @@ROWCOUNT AS CachedEntries;
END;
GO
```

### SP: Get Persoane cu Filtrare și Paginare

```sql
-- =============================================
-- PROCEDURE: Returnează Persoane filtrate și paginate
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[sp_GetPersoane]
    @UserId UNIQUEIDENTIFIER,
    @SearchTerm NVARCHAR(100) = NULL,
    @CompanyId UNIQUEIDENTIFIER = NULL,  -- Filtru adițional
    @PageNumber INT = 1,
    @PageSize INT = 50,
    @SortColumn NVARCHAR(50) = 'NumeComplet',
    @SortDirection NVARCHAR(4) = 'ASC'
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Verifică dacă e admin
    DECLARE @IsAdmin BIT = 0;
    IF EXISTS (
        SELECT 1 FROM UserRoles ur 
        INNER JOIN Roles r ON ur.RoleId = r.Id 
        WHERE ur.UserId = @UserId AND r.Name = 'Admin'
    )
        SET @IsAdmin = 1;
    
    -- Query principal cu dynamic sorting
    ;WITH FilteredData AS (
        SELECT 
            p.*,
            c.Name AS CompanyName,
            ROW_NUMBER() OVER (
                ORDER BY 
                    CASE WHEN @SortDirection = 'ASC' THEN
                        CASE @SortColumn 
                            WHEN 'NumeComplet' THEN p.NumeComplet
                            WHEN 'CompanyName' THEN c.Name
                        END
                    END ASC,
                    CASE WHEN @SortDirection = 'DESC' THEN
                        CASE @SortColumn 
                            WHEN 'NumeComplet' THEN p.NumeComplet
                            WHEN 'CompanyName' THEN c.Name
                        END
                    END DESC
            ) AS RowNum,
            COUNT(*) OVER() AS TotalCount
        FROM Persoane p
        LEFT JOIN Companies c ON p.OwnerCompanyId = c.Id
        WHERE p.IsActive = 1
        AND (
            @IsAdmin = 1
            OR p.OwnerCompanyId IN (SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(@UserId))
            OR p.CreatedBy = @UserId
        )
        AND (@SearchTerm IS NULL OR p.NumeComplet LIKE '%' + @SearchTerm + '%')
        AND (@CompanyId IS NULL OR p.OwnerCompanyId = @CompanyId)
    )
    SELECT *
    FROM FilteredData
    WHERE RowNum BETWEEN ((@PageNumber - 1) * @PageSize + 1) AND (@PageNumber * @PageSize)
    ORDER BY RowNum;
END;
GO
```

### SP: Verificare Acces la Entitate

```sql
-- =============================================
-- PROCEDURE: Verifică dacă un user are acces la o entitate
-- Returnează AccessLevel sau 0 dacă nu are acces
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[sp_CheckUserAccess]
    @UserId UNIQUEIDENTIFIER,
    @EntityType VARCHAR(20),
    @EntityId UNIQUEIDENTIFIER,
    @RequiredAccessLevel TINYINT = 1  -- Default: Read
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @ActualAccessLevel TINYINT = 0;
    DECLARE @IsAdmin BIT = 0;
    
    -- Check admin
    IF EXISTS (
        SELECT 1 FROM UserRoles ur 
        INNER JOIN Roles r ON ur.RoleId = r.Id 
        WHERE ur.UserId = @UserId AND r.Name = 'Admin'
    )
    BEGIN
        SELECT 
            @IsAdmin AS IsAdmin,
            4 AS AccessLevel,  -- Admin = full access
            1 AS HasAccess,
            'Admin override' AS Reason;
        RETURN;
    END
    
    -- Check based on entity type
    IF @EntityType = 'LOCATION'
        SELECT @ActualAccessLevel = ISNULL(AccessLevel, 0)
        FROM dbo.fn_GetUserVisibleLocations(@UserId)
        WHERE LocationId = @EntityId;
    ELSE IF @EntityType = 'WORKPLACE'
        SELECT @ActualAccessLevel = ISNULL(AccessLevel, 0)
        FROM dbo.fn_GetUserVisibleWorkPlaces(@UserId)
        WHERE WorkPlaceId = @EntityId;
    ELSE IF @EntityType = 'COMPANY'
        SELECT @ActualAccessLevel = ISNULL(AccessLevel, 0)
        FROM dbo.fn_GetUserVisibleCompanies(@UserId)
        WHERE CompanyId = @EntityId;
    ELSE IF @EntityType = 'GROUP'
        SELECT @ActualAccessLevel = ISNULL(AccessLevel, 0)
        FROM dbo.fn_GetUserVisibleGroups(@UserId)
        WHERE GroupId = @EntityId;
    
    SELECT 
        @IsAdmin AS IsAdmin,
        @ActualAccessLevel AS AccessLevel,
        CASE WHEN @ActualAccessLevel >= @RequiredAccessLevel THEN 1 ELSE 0 END AS HasAccess,
        CASE 
            WHEN @ActualAccessLevel = 0 THEN 'No access'
            WHEN @ActualAccessLevel < @RequiredAccessLevel THEN 'Insufficient access level'
            ELSE 'Access granted'
        END AS Reason;
END;
GO
```

### SP: CRUD pentru UserOrganizationalAccess

```sql
-- =============================================
-- PROCEDURE: Adaugă acces pentru un user
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[sp_AddUserOrganizationalAccess]
    @UserId UNIQUEIDENTIFIER,
    @EntityType VARCHAR(20),
    @EntityId UNIQUEIDENTIFIER,
    @AccessLevel TINYINT = 1,
    @InheritToChildren BIT = 1,
    @ValidFrom DATETIME2 = NULL,
    @ValidTo DATETIME2 = NULL,
    @Notes NVARCHAR(500) = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Verifică dacă există deja (activ)
    IF EXISTS (
        SELECT 1 FROM UserOrganizationalAccess 
        WHERE UserId = @UserId 
          AND EntityType = @EntityType 
          AND EntityId = @EntityId 
          AND IsActive = 1
    )
    BEGIN
        -- Update în loc de insert
        UPDATE UserOrganizationalAccess
        SET AccessLevel = @AccessLevel,
            InheritToChildren = @InheritToChildren,
            ValidFrom = @ValidFrom,
            ValidTo = @ValidTo,
            Notes = @Notes,
            UpdatedAt = GETDATE(),
            UpdatedBy = @CreatedBy
        WHERE UserId = @UserId 
          AND EntityType = @EntityType 
          AND EntityId = @EntityId 
          AND IsActive = 1;
        
        SELECT 'Updated' AS Action, @@ROWCOUNT AS AffectedRows;
    END
    ELSE
    BEGIN
        INSERT INTO UserOrganizationalAccess 
            (UserId, EntityType, EntityId, AccessLevel, InheritToChildren, ValidFrom, ValidTo, Notes, CreatedBy)
        VALUES 
            (@UserId, @EntityType, @EntityId, @AccessLevel, @InheritToChildren, @ValidFrom, @ValidTo, @Notes, @CreatedBy);
        
        SELECT 'Inserted' AS Action, 1 AS AffectedRows;
    END
    
    -- Refresh cache
    EXEC sp_RefreshUserVisibleEntitiesCache @UserId;
END;
GO

-- =============================================
-- PROCEDURE: Revocă acces pentru un user
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[sp_RevokeUserOrganizationalAccess]
    @UserId UNIQUEIDENTIFIER,
    @EntityType VARCHAR(20),
    @EntityId UNIQUEIDENTIFIER,
    @RevokedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE UserOrganizationalAccess
    SET IsActive = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @RevokedBy
    WHERE UserId = @UserId 
      AND EntityType = @EntityType 
      AND EntityId = @EntityId 
      AND IsActive = 1;
    
    SELECT @@ROWCOUNT AS RevokedCount;
    
    -- Refresh cache
    EXEC sp_RefreshUserVisibleEntitiesCache @UserId;
END;
GO
```

---

## 🖥️ Implementare C# (Dapper)

### Model: UserPerimeter

```csharp
// Models/Security/UserPerimeter.cs
namespace ValyanERP.Models.Security;

public class UserPerimeter
{
    public Guid UserId { get; set; }
    public bool HasFullAccess { get; set; }
    
    public HashSet<Guid> VisibleGroupIds { get; set; } = new();
    public HashSet<Guid> VisibleCompanyIds { get; set; } = new();
    public HashSet<Guid> VisibleWorkPlaceIds { get; set; } = new();
    public HashSet<Guid> VisibleLocationIds { get; set; } = new();
    
    // Access levels per entity (pentru verificări granulare)
    public Dictionary<Guid, byte> CompanyAccessLevels { get; set; } = new();
    public Dictionary<Guid, byte> LocationAccessLevels { get; set; } = new();
    
    public DateTime CachedAt { get; set; }
    
    // Helper methods
    public bool CanAccessCompany(Guid companyId) => 
        HasFullAccess || VisibleCompanyIds.Contains(companyId);
    
    public bool CanAccessLocation(Guid locationId) => 
        HasFullAccess || VisibleLocationIds.Contains(locationId);
    
    public bool CanWriteToCompany(Guid companyId) => 
        HasFullAccess || (CompanyAccessLevels.TryGetValue(companyId, out var level) && level >= 2);
    
    public bool CanWriteToLocation(Guid locationId) => 
        HasFullAccess || (LocationAccessLevels.TryGetValue(locationId, out var level) && level >= 2);
}
```

### Model: WorkingContext (Context Activ)

```csharp
// Models/Security/WorkingContext.cs
namespace ValyanERP.Models.Security;

public class WorkingContext
{
    public Guid? ActiveGroupId { get; set; }
    public Guid? ActiveCompanyId { get; set; }
    public Guid? ActiveWorkPlaceId { get; set; }
    public Guid? ActiveLocationId { get; set; }
    
    // Denumiri pentru afișare în UI
    public string? ActiveGroupName { get; set; }
    public string? ActiveCompanyName { get; set; }
    public string? ActiveWorkPlaceName { get; set; }
    public string? ActiveLocationName { get; set; }
}
```

### Interface: ISecureConnectionFactory

```csharp
// Data/ISecureConnectionFactory.cs
namespace ValyanERP.Data;

public interface ISecureConnectionFactory
{
    /// <summary>
    /// Creează o conexiune cu SESSION_CONTEXT setat pentru user-ul curent
    /// </summary>
    Task<IDbConnection> CreateSecureConnectionAsync();
    
    /// <summary>
    /// Creează o conexiune fără SESSION_CONTEXT (pentru operații de sistem)
    /// </summary>
    IDbConnection CreateSystemConnection();
}
```

### Implementare: SecureConnectionFactory

```csharp
// Data/SecureConnectionFactory.cs
using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;

namespace ValyanERP.Data;

public class SecureConnectionFactory : ISecureConnectionFactory
{
    private readonly string _connectionString;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<SecureConnectionFactory> _logger;
    
    public SecureConnectionFactory(
        IConfiguration configuration,
        ICurrentUserService currentUserService,
        ILogger<SecureConnectionFactory> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string not configured");
        _currentUserService = currentUserService;
        _logger = logger;
    }
    
    public async Task<IDbConnection> CreateSecureConnectionAsync()
    {
        var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        try
        {
            var userId = _currentUserService.UserId;
            var isAdmin = await _currentUserService.IsInRoleAsync("Admin");
            
            // Setează SESSION_CONTEXT
            await connection.ExecuteAsync(@"
                EXEC sp_set_session_context @key = N'UserId', @value = @UserId;
                EXEC sp_set_session_context @key = N'IsAdmin', @value = @IsAdmin;",
                new { UserId = userId, IsAdmin = isAdmin ? 1 : 0 });
            
            _logger.LogDebug("Secure connection created for user {UserId}, IsAdmin: {IsAdmin}", userId, isAdmin);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set SESSION_CONTEXT");
            connection.Dispose();
            throw;
        }
        
        return connection;
    }
    
    public IDbConnection CreateSystemConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
```

### Interface: IUserPerimeterProvider

```csharp
// Services/Security/IUserPerimeterProvider.cs
namespace ValyanERP.Services.Security;

public interface IUserPerimeterProvider
{
    Task<UserPerimeter> GetPerimeterAsync();
    Task<IEnumerable<Guid>> GetVisibleCompanyIdsAsync();
    Task<IEnumerable<Guid>> GetVisibleLocationIdsAsync();
    Task<bool> CanAccessAsync(string entityType, Guid entityId, byte requiredLevel = 1);
    Task InvalidateCacheAsync();
}
```

### Implementare: UserPerimeterProvider

```csharp
// Services/Security/UserPerimeterProvider.cs
using System.Data;
using Dapper;
using Microsoft.Extensions.Caching.Memory;

namespace ValyanERP.Services.Security;

public class UserPerimeterProvider : IUserPerimeterProvider
{
    private readonly ISecureConnectionFactory _connectionFactory;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UserPerimeterProvider> _logger;
    
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    
    public UserPerimeterProvider(
        ISecureConnectionFactory connectionFactory,
        ICurrentUserService currentUserService,
        IMemoryCache cache,
        ILogger<UserPerimeterProvider> logger)
    {
        _connectionFactory = connectionFactory;
        _currentUserService = currentUserService;
        _cache = cache;
        _logger = logger;
    }
    
    private string CacheKey => $"UserPerimeter_{_currentUserService.UserId}";
    
    public async Task<UserPerimeter> GetPerimeterAsync()
    {
        if (_cache.TryGetValue(CacheKey, out UserPerimeter? cached) && cached != null)
            return cached;
        
        var userId = _currentUserService.UserId;
        var perimeter = new UserPerimeter
        {
            UserId = userId,
            CachedAt = DateTime.UtcNow
        };
        
        // Check admin status
        perimeter.HasFullAccess = await _currentUserService.IsInRoleAsync("Admin");
        
        if (!perimeter.HasFullAccess)
        {
            using var db = _connectionFactory.CreateSystemConnection();
            
            // Load visible companies with access levels
            var companies = await db.QueryAsync<(Guid CompanyId, byte AccessLevel)>(
                "SELECT CompanyId, AccessLevel FROM dbo.fn_GetUserVisibleCompanies(@UserId)",
                new { UserId = userId });
            
            foreach (var (companyId, accessLevel) in companies)
            {
                perimeter.VisibleCompanyIds.Add(companyId);
                perimeter.CompanyAccessLevels[companyId] = accessLevel;
            }
            
            // Load visible locations with access levels
            var locations = await db.QueryAsync<(Guid LocationId, byte AccessLevel)>(
                "SELECT LocationId, AccessLevel FROM dbo.fn_GetUserVisibleLocations(@UserId)",
                new { UserId = userId });
            
            foreach (var (locationId, accessLevel) in locations)
            {
                perimeter.VisibleLocationIds.Add(locationId);
                perimeter.LocationAccessLevels[locationId] = accessLevel;
            }
            
            // Load visible workplaces
            var workplaces = await db.QueryAsync<Guid>(
                "SELECT WorkPlaceId FROM dbo.fn_GetUserVisibleWorkPlaces(@UserId)",
                new { UserId = userId });
            perimeter.VisibleWorkPlaceIds = workplaces.ToHashSet();
            
            // Load visible groups
            var groups = await db.QueryAsync<Guid>(
                "SELECT GroupId FROM dbo.fn_GetUserVisibleGroups(@UserId)",
                new { UserId = userId });
            perimeter.VisibleGroupIds = groups.ToHashSet();
        }
        
        _cache.Set(CacheKey, perimeter, CacheDuration);
        _logger.LogInformation(
            "Loaded perimeter for user {UserId}: {CompanyCount} companies, {LocationCount} locations",
            userId, perimeter.VisibleCompanyIds.Count, perimeter.VisibleLocationIds.Count);
        
        return perimeter;
    }
    
    public async Task<IEnumerable<Guid>> GetVisibleCompanyIdsAsync()
    {
        var perimeter = await GetPerimeterAsync();
        
        if (perimeter.HasFullAccess)
        {
            using var db = _connectionFactory.CreateSystemConnection();
            return await db.QueryAsync<Guid>("SELECT Id FROM Companies WHERE IsActive = 1");
        }
        
        return perimeter.VisibleCompanyIds;
    }
    
    public async Task<IEnumerable<Guid>> GetVisibleLocationIdsAsync()
    {
        var perimeter = await GetPerimeterAsync();
        
        if (perimeter.HasFullAccess)
        {
            using var db = _connectionFactory.CreateSystemConnection();
            return await db.QueryAsync<Guid>("SELECT Id FROM Locations WHERE IsActive = 1");
        }
        
        return perimeter.VisibleLocationIds;
    }
    
    public async Task<bool> CanAccessAsync(string entityType, Guid entityId, byte requiredLevel = 1)
    {
        var perimeter = await GetPerimeterAsync();
        
        if (perimeter.HasFullAccess)
            return true;
        
        return entityType.ToUpperInvariant() switch
        {
            "COMPANY" => perimeter.CompanyAccessLevels.TryGetValue(entityId, out var cl) && cl >= requiredLevel,
            "LOCATION" => perimeter.LocationAccessLevels.TryGetValue(entityId, out var ll) && ll >= requiredLevel,
            "WORKPLACE" => perimeter.VisibleWorkPlaceIds.Contains(entityId),
            "GROUP" => perimeter.VisibleGroupIds.Contains(entityId),
            _ => false
        };
    }
    
    public Task InvalidateCacheAsync()
    {
        _cache.Remove(CacheKey);
        _logger.LogInformation("Perimeter cache invalidated for user {UserId}", _currentUserService.UserId);
        return Task.CompletedTask;
    }
}
```

### Base Class: SecuredRepositoryBase

```csharp
// Data/Repositories/SecuredRepositoryBase.cs
using System.Data;
using Dapper;

namespace ValyanERP.Data.Repositories;

public abstract class SecuredRepositoryBase<T> where T : class
{
    protected readonly ISecureConnectionFactory _connectionFactory;
    protected readonly IUserPerimeterProvider _perimeterProvider;
    protected readonly ICurrentUserService _currentUserService;
    protected readonly ILogger _logger;
    
    protected SecuredRepositoryBase(
        ISecureConnectionFactory connectionFactory,
        IUserPerimeterProvider perimeterProvider,
        ICurrentUserService currentUserService,
        ILogger logger)
    {
        _connectionFactory = connectionFactory;
        _perimeterProvider = perimeterProvider;
        _currentUserService = currentUserService;
        _logger = logger;
    }
    
    /// <summary>
    /// Numele tabelului pentru query-uri
    /// </summary>
    protected abstract string TableName { get; }
    
    /// <summary>
    /// View-ul filtrat (optional, dacă există)
    /// </summary>
    protected virtual string? FilteredViewName => null;
    
    /// <summary>
    /// Numele coloanei de ownership (default: OwnerCompanyId)
    /// </summary>
    protected virtual string OwnershipColumn => "OwnerCompanyId";
    
    /// <summary>
    /// Query-ul pentru filtrare manuală (când nu folosim view)
    /// </summary>
    protected virtual string SecurityFilter => $@"
        ({OwnershipColumn} IN @VisibleCompanyIds OR CreatedBy = @CurrentUserId)";
    
    /// <summary>
    /// Obține toate înregistrările vizibile pentru user-ul curent
    /// </summary>
    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        var perimeter = await _perimeterProvider.GetPerimeterAsync();
        
        // Folosește view-ul filtrat dacă există
        if (!string.IsNullOrEmpty(FilteredViewName))
        {
            using var db = await _connectionFactory.CreateSecureConnectionAsync();
            return await db.QueryAsync<T>($"SELECT * FROM {FilteredViewName}");
        }
        
        // Altfel, aplică filtrarea manuală
        using (var db = _connectionFactory.CreateSystemConnection())
        {
            var sql = $@"
                SELECT * FROM {TableName}
                WHERE IsActive = 1 AND {SecurityFilter}";
            
            return await db.QueryAsync<T>(sql, new
            {
                VisibleCompanyIds = perimeter.VisibleCompanyIds.ToList(),
                CurrentUserId = _currentUserService.UserId
            });
        }
    }
    
    /// <summary>
    /// Obține o înregistrare cu verificare de acces
    /// </summary>
    public virtual async Task<T?> GetByIdAsync(Guid id)
    {
        using var db = await _connectionFactory.CreateSecureConnectionAsync();
        
        var source = !string.IsNullOrEmpty(FilteredViewName) ? FilteredViewName : TableName;
        var item = await db.QueryFirstOrDefaultAsync<T>(
            $"SELECT * FROM {source} WHERE Id = @Id AND IsActive = 1",
            new { Id = id });
        
        return item;
    }
    
    /// <summary>
    /// Verifică accesul înainte de modificare
    /// </summary>
    protected async Task EnsureWriteAccessAsync(Guid entityCompanyId)
    {
        var canWrite = await _perimeterProvider.CanAccessAsync("COMPANY", entityCompanyId, 2);
        
        if (!canWrite)
        {
            _logger.LogWarning(
                "Access denied: User {UserId} attempted write on company {CompanyId}",
                _currentUserService.UserId, entityCompanyId);
            
            throw new UnauthorizedAccessException("Nu aveți permisiune de scriere pentru această entitate.");
        }
    }
}
```

### Exemplu: PersoaneRepository

```csharp
// Data/Repositories/PersoaneRepository.cs
using System.Data;
using Dapper;

namespace ValyanERP.Data.Repositories;

public interface IPersoaneRepository
{
    Task<IEnumerable<Persoana>> GetAllAsync();
    Task<Persoana?> GetByIdAsync(Guid id);
    Task<PagedResult<PersoanaListDto>> GetPagedAsync(PersoaneFilter filter);
    Task<Guid> CreateAsync(Persoana persoana);
    Task UpdateAsync(Persoana persoana);
    Task DeleteAsync(Guid id);
}

public class PersoaneRepository : SecuredRepositoryBase<Persoana>, IPersoaneRepository
{
    public PersoaneRepository(
        ISecureConnectionFactory connectionFactory,
        IUserPerimeterProvider perimeterProvider,
        ICurrentUserService currentUserService,
        ILogger<PersoaneRepository> logger)
        : base(connectionFactory, perimeterProvider, currentUserService, logger)
    {
    }
    
    protected override string TableName => "Persoane";
    protected override string? FilteredViewName => "vw_Persoane_Filtered";
    
    public async Task<PagedResult<PersoanaListDto>> GetPagedAsync(PersoaneFilter filter)
    {
        using var db = await _connectionFactory.CreateSecureConnectionAsync();
        
        var result = await db.QueryAsync<PersoanaListDto>(
            "sp_GetPersoane",
            new
            {
                UserId = _currentUserService.UserId,
                SearchTerm = filter.SearchTerm,
                CompanyId = filter.CompanyId,
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
                SortColumn = filter.SortColumn ?? "NumeComplet",
                SortDirection = filter.SortDirection ?? "ASC"
            },
            commandType: CommandType.StoredProcedure);
        
        var items = result.ToList();
        var totalCount = items.FirstOrDefault()?.TotalCount ?? 0;
        
        return new PagedResult<PersoanaListDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize
        };
    }
    
    public async Task<Guid> CreateAsync(Persoana persoana)
    {
        // Verifică acces la compania owner
        if (persoana.OwnerCompanyId.HasValue)
            await EnsureWriteAccessAsync(persoana.OwnerCompanyId.Value);
        
        using var db = _connectionFactory.CreateSystemConnection();
        
        persoana.Id = Guid.NewGuid();
        persoana.CreatedBy = _currentUserService.UserId;
        persoana.CreatedAt = DateTime.UtcNow;
        
        await db.ExecuteAsync(@"
            INSERT INTO Persoane (Id, NumeComplet, Email, Telefon, OwnerCompanyId, CreatedBy, CreatedAt, IsActive)
            VALUES (@Id, @NumeComplet, @Email, @Telefon, @OwnerCompanyId, @CreatedBy, @CreatedAt, 1)",
            persoana);
        
        return persoana.Id;
    }
    
    public async Task UpdateAsync(Persoana persoana)
    {
        // Verifică că are acces la înregistrarea existentă
        var existing = await GetByIdAsync(persoana.Id);
        if (existing == null)
            throw new KeyNotFoundException($"Persoana {persoana.Id} nu a fost găsită sau nu aveți acces.");
        
        // Verifică acces de scriere
        if (existing.OwnerCompanyId.HasValue)
            await EnsureWriteAccessAsync(existing.OwnerCompanyId.Value);
        
        using var db = _connectionFactory.CreateSystemConnection();
        
        persoana.UpdatedBy = _currentUserService.UserId;
        persoana.UpdatedAt = DateTime.UtcNow;
        
        await db.ExecuteAsync(@"
            UPDATE Persoane 
            SET NumeComplet = @NumeComplet, 
                Email = @Email, 
                Telefon = @Telefon,
                UpdatedBy = @UpdatedBy,
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id",
            persoana);
    }
    
    public async Task DeleteAsync(Guid id)
    {
        var existing = await GetByIdAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"Persoana {id} nu a fost găsită sau nu aveți acces.");
        
        if (existing.OwnerCompanyId.HasValue)
            await EnsureWriteAccessAsync(existing.OwnerCompanyId.Value);
        
        using var db = _connectionFactory.CreateSystemConnection();
        
        await db.ExecuteAsync(@"
            UPDATE Persoane 
            SET IsActive = 0, 
                UpdatedBy = @UpdatedBy, 
                UpdatedAt = @UpdatedAt
            WHERE Id = @Id",
            new
            {
                Id = id,
                UpdatedBy = _currentUserService.UserId,
                UpdatedAt = DateTime.UtcNow
            });
    }
}
```

### Extension: Table-Valued Parameter Helper

```csharp
// Extensions/DapperExtensions.cs
using System.Data;
using Microsoft.Data.SqlClient;

namespace ValyanERP.Extensions;

public static class DapperExtensions
{
    /// <summary>
    /// Convertește o listă de GUID-uri în Table-Valued Parameter
    /// </summary>
    public static SqlMapper.ICustomQueryParameter AsGuidTableValuedParameter(
        this IEnumerable<Guid> guids, 
        string typeName = "GuidList")
    {
        var dt = new DataTable();
        dt.Columns.Add("Id", typeof(Guid));
        
        foreach (var guid in guids)
            dt.Rows.Add(guid);
        
        return dt.AsTableValuedParameter(typeName);
    }
    
    /// <summary>
    /// Adaugă filtru de securitate la un query SQL
    /// </summary>
    public static string WithCompanyFilter(
        this string sql, 
        string tableAlias = "", 
        string ownerColumn = "OwnerCompanyId")
    {
        var prefix = string.IsNullOrEmpty(tableAlias) ? "" : $"{tableAlias}.";
        return $@"{sql} 
            AND {prefix}{ownerColumn} IN (
                SELECT CompanyId FROM dbo.fn_GetUserVisibleCompanies(@CurrentUserId)
            )";
    }
}
```

### Service Registration

```csharp
// Program.cs sau Extensions/ServiceCollectionExtensions.cs
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrganizationalSecurity(this IServiceCollection services)
    {
        // Conexiuni
        services.AddScoped<ISecureConnectionFactory, SecureConnectionFactory>();
        
        // Perimeter provider (scoped - per request)
        services.AddScoped<IUserPerimeterProvider, UserPerimeterProvider>();
        
        // Memory cache pentru perimeter
        services.AddMemoryCache();
        
        // Repositories
        services.AddScoped<IPersoaneRepository, PersoaneRepository>();
        // ... alte repositories
        
        return services;
    }
}
```

---

## 🔄 Context Switching (Selector Companie/Locație Activă)

### Service: WorkingContextService

```csharp
// Services/WorkingContextService.cs
namespace ValyanERP.Services;

public interface IWorkingContextService
{
    WorkingContext Current { get; }
    Task SetActiveCompanyAsync(Guid companyId);
    Task SetActiveLocationAsync(Guid locationId);
    Task<IEnumerable<CompanyDto>> GetAvailableCompaniesAsync();
    Task<IEnumerable<LocationDto>> GetAvailableLocationsAsync();
}

public class WorkingContextService : IWorkingContextService
{
    private readonly IUserPerimeterProvider _perimeterProvider;
    private readonly ISessionService _sessionService;
    private readonly ISecureConnectionFactory _connectionFactory;
    
    private const string SessionKey = "WorkingContext";
    
    public WorkingContextService(
        IUserPerimeterProvider perimeterProvider,
        ISessionService sessionService,
        ISecureConnectionFactory connectionFactory)
    {
        _perimeterProvider = perimeterProvider;
        _sessionService = sessionService;
        _connectionFactory = connectionFactory;
    }
    
    public WorkingContext Current => 
        _sessionService.Get<WorkingContext>(SessionKey) ?? new WorkingContext();
    
    public async Task SetActiveCompanyAsync(Guid companyId)
    {
        // Verifică că are acces
        var perimeter = await _perimeterProvider.GetPerimeterAsync();
        if (!perimeter.CanAccessCompany(companyId))
            throw new UnauthorizedAccessException("Nu aveți acces la această companie.");
        
        using var db = _connectionFactory.CreateSystemConnection();
        var company = await db.QueryFirstAsync<CompanyDto>(
            "SELECT Id, Name FROM Companies WHERE Id = @Id",
            new { Id = companyId });
        
        var context = Current;
        context.ActiveCompanyId = companyId;
        context.ActiveCompanyName = company.Name;
        context.ActiveWorkPlaceId = null;
        context.ActiveLocationId = null;
        
        _sessionService.Set(SessionKey, context);
    }
    
    public async Task SetActiveLocationAsync(Guid locationId)
    {
        var perimeter = await _perimeterProvider.GetPerimeterAsync();
        if (!perimeter.CanAccessLocation(locationId))
            throw new UnauthorizedAccessException("Nu aveți acces la această locație.");
        
        using var db = _connectionFactory.CreateSystemConnection();
        var location = await db.QueryFirstAsync<LocationWithHierarchyDto>(@"
            SELECT l.Id, l.Name, l.CompanyId, c.Name AS CompanyName, 
                   l.WorkPlaceId, wp.Name AS WorkPlaceName
            FROM Locations l
            INNER JOIN Companies c ON l.CompanyId = c.Id
            LEFT JOIN WorkPlaces wp ON l.WorkPlaceId = wp.Id
            WHERE l.Id = @Id",
            new { Id = locationId });
        
        var context = Current;
        context.ActiveCompanyId = location.CompanyId;
        context.ActiveCompanyName = location.CompanyName;
        context.ActiveWorkPlaceId = location.WorkPlaceId;
        context.ActiveWorkPlaceName = location.WorkPlaceName;
        context.ActiveLocationId = locationId;
        context.ActiveLocationName = location.Name;
        
        _sessionService.Set(SessionKey, context);
    }
    
    public async Task<IEnumerable<CompanyDto>> GetAvailableCompaniesAsync()
    {
        var companyIds = await _perimeterProvider.GetVisibleCompanyIdsAsync();
        
        using var db = _connectionFactory.CreateSystemConnection();
        return await db.QueryAsync<CompanyDto>(@"
            SELECT Id, Name, CUI, GroupId 
            FROM Companies 
            WHERE Id IN @Ids AND IsActive = 1
            ORDER BY Name",
            new { Ids = companyIds.ToList() });
    }
    
    public async Task<IEnumerable<LocationDto>> GetAvailableLocationsAsync()
    {
        var locationIds = await _perimeterProvider.GetVisibleLocationIdsAsync();
        
        using var db = _connectionFactory.CreateSystemConnection();
        return await db.QueryAsync<LocationDto>(@"
            SELECT l.Id, l.Name, l.CompanyId, c.Name AS CompanyName
            FROM Locations l
            INNER JOIN Companies c ON l.CompanyId = c.Id
            WHERE l.Id IN @Ids AND l.IsActive = 1
            ORDER BY c.Name, l.Name",
            new { Ids = locationIds.ToList() });
    }
}
```

---

## 🧪 Scenarii de Test

### Scenariul 1: Admin General
```
User: admin@valyanerp.ro
Role: Admin

Perimeter calculat:
  - HasFullAccess = true
  - Vede TOATE entitățile din sistem
  - Bypass complet al filtrării
```

### Scenariul 2: Manager Grup
```
User: manager.grup@uti.com
Access:
  - EntityType: GROUP, EntityId: [UTI_GRUP_ID], AccessLevel: 3, InheritToChildren: true

Perimeter calculat:
  - VisibleGroupIds: [UTI_GRUP_ID]
  - VisibleCompanyIds: [toate companiile din UTI GRUP]
  - VisibleWorkPlaceIds: [toate punctele de lucru]
  - VisibleLocationIds: [toate locațiile]
```

### Scenariul 3: Manager Companie
```
User: director@uti-systems.ro
Access:
  - EntityType: COMPANY, EntityId: [UTI_SYSTEMS_ID], AccessLevel: 3, InheritToChildren: true

Perimeter calculat:
  - VisibleCompanyIds: [UTI_SYSTEMS_ID]
  - VisibleWorkPlaceIds: [Sediu Central, Filiala Cluj, Filiala Iași]
  - VisibleLocationIds: [toate locațiile din UTI SYSTEMS]
```

### Scenariul 4: Manager cu Excludere
```
User: manager.partial@uti.com
Access:
  - EntityType: COMPANY, EntityId: [UTI_SYSTEMS_ID], AccessLevel: 3, InheritToChildren: true
  - EntityType: LOCATION, EntityId: [SALA_DISPECERAT_ID], AccessLevel: 0 (DENY!)

Perimeter calculat:
  - Vede tot din UTI SYSTEMS EXCEPTÂND Sala Dispecerat
```

### Scenariul 5: Operator Locații Multiple
```
User: operator.depozite@uti.com
Access:
  - EntityType: LOCATION, EntityId: [DEPOZIT_CENTRAL_ID], AccessLevel: 2
  - EntityType: LOCATION, EntityId: [DEPOZIT_ECHIPAMENTE_ID], AccessLevel: 2

Perimeter calculat:
  - VisibleLocationIds: [DEPOZIT_CENTRAL_ID, DEPOZIT_ECHIPAMENTE_ID]
  - NU vede alte locații
```

### Scenariul 6: Acces Temporar (Contractor)
```
User: contractor@extern.com
Access:
  - EntityType: WORKPLACE, EntityId: [FILIALA_CLUJ_ID], AccessLevel: 1
    ValidFrom: 2026-01-01, ValidTo: 2026-03-31

Perimeter calculat:
  - Între 01.01.2026 - 31.03.2026: vede Filiala Cluj și locațiile din ea
  - După 31.03.2026: perimetru gol
```

---

## 🔒 Considerații de Securitate

### 1. Defense in Depth
- Filtrare la nivel SQL (views cu SESSION_CONTEXT)
- Filtrare la nivel repository (WHERE cu perimeter)
- Verificare la nivel service înainte de operații

### 2. Audit Trail
```sql
-- Trigger pentru audit modificări permisiuni
CREATE TRIGGER [TR_UOA_Audit] ON [UserOrganizationalAccess]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    INSERT INTO AuditLogs (TableName, RecordId, Action, OldValues, NewValues, UserId, CreatedAt)
    SELECT 
        'UserOrganizationalAccess',
        COALESCE(i.Id, d.Id),
        CASE 
            WHEN i.Id IS NOT NULL AND d.Id IS NULL THEN 'INSERT'
            WHEN i.Id IS NOT NULL AND d.Id IS NOT NULL THEN 'UPDATE'
            ELSE 'DELETE'
        END,
        (SELECT * FROM deleted FOR JSON PATH),
        (SELECT * FROM inserted FOR JSON PATH),
        COALESCE(i.UpdatedBy, i.CreatedBy, CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)),
        GETDATE()
    FROM inserted i
    FULL OUTER JOIN deleted d ON i.Id = d.Id;
END;
```

### 3. Cache Invalidation via SignalR
```csharp
// Când se modifică permisiunile unui user
public async Task OnPermissionsChangedAsync(Guid userId)
{
    // Invalidează cache-ul
    await _perimeterProvider.InvalidateCacheAsync(userId);
    
    // Notifică user-ul via SignalR
    await _hubContext.Clients.User(userId.ToString())
        .SendAsync("PermissionsChanged", "Permisiunile dvs. au fost actualizate.");
}
```

---

## 📋 Plan de Implementare

### FAZA 1: Baza de Date ✅ COMPLETĂ
- [x] Script `022_UserOrganizationalAccess.sql`
  - Tabel principal
  - Funcții TVF (GetUserVisibleLocations, Companies, WorkPlaces, Groups)
  - Funcție fn_UserHasAccessToEntity
  - View vw_UserOrganizationalPerimeter
- [x] Script `023_StoredProcedures_OrganizationalAccess.sql`
  - CRUD: Create, Update, Delete, GetById, GetByUser
  - BulkCreate cu MERGE
  - GetPerimeter, GetPerimeterJson
  - GetUsersWithAccess, CopyFromUser
  - OrganizationTree_WithUserAccess
- [x] Script `024_AddOwnershipColumns.sql`
  - Coloane ownership în Persoane, Users, Partners
  - FK constraints și indexuri
- [x] Script `026_UpdateStoredProcedures_WithOwnership.sql`
  - SP-uri actualizate cu parametri ownership
- [x] Script `027_OrganizationalAccess_Enhancements.sql`
  - UserVisibleEntitiesCache (cache performanță)
  - EntitySharing (partajare între companii)
  - AccessDeniedLog (audit securitate)
  - GuidList TYPE (TVP pentru Dapper)
  - Views filtrate: vw_Persoane_Filtered, vw_Partners_Filtered, vw_Users_Filtered
  - Trigger TR_UOA_Audit
  - SP-uri: RefreshUserVisibleEntitiesCache, CheckUserAccess, EntitySharing CRUD
  - Statistici și cleanup

### FAZA 2: Backend - Dapper (Estimare: 5-6 ore)
- [ ] Model: `UserOrganizationalAccess.cs`
- [ ] Model: `UserPerimeter.cs` (cu HashSet pentru lookup rapid)
- [ ] Model: `WorkingContext.cs`
- [ ] Service: `ISecureConnectionFactory` + implementare
- [ ] Service: `IUserPerimeterProvider` + implementare (cu IMemoryCache)
- [ ] Service: `IWorkingContextService` + implementare
- [ ] Base class: `SecuredRepositoryBase<T>`
- [ ] Extension: `DapperExtensions.cs` (TVP, security filter)
- [ ] Integrare în `Program.cs` (DI registration)

### FAZA 3: Migrare Repositories (Estimare: 4-5 ore)
- [ ] Modificare `PersoaneRepository` să extindă `SecuredRepositoryBase`
- [ ] Modificare `PartnersRepository`
- [ ] Modificare alte repositories operaționale
- [ ] Testare manuală cu useri diferiți

### FAZA 4: UI Administrare (Estimare: 6-8 ore)
- [ ] Pagină `/administrare/permisiuni-organizationale`
- [ ] Componentă TreeView pentru structura organizațională (Syncfusion SfTreeView)
- [ ] Dialog pentru configurare acces user
- [ ] Componentă pentru selector context activ (în header/navbar)
- [ ] Integrare cu SfDataGrid pentru lista utilizatori

### FAZA 5: Testing (Estimare: 3-4 ore)
- [ ] Unit tests pentru `UserPerimeterProvider`
- [ ] Integration tests pentru repositories cu filtrare
- [ ] Manual testing cu scenariile definite
- [ ] Test performanță cu date de volum

### FAZA 6: Documentație (Estimare: 2 ore)
- [ ] README pentru sistemul de permisiuni
- [ ] Ghid administrare pentru utilizatori

---

## 📂 Scripturi SQL - Ordine Execuție

```powershell
# Execută în această ordine:

# 1. Tabele organizaționale (dacă nu există deja)
Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "017_SocietateaProprie.sql"

# 2. Tabel principal UserOrganizationalAccess + Funcții TVF
Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "022_UserOrganizationalAccess.sql"

# 3. Stored Procedures CRUD pentru permisiuni
Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "023_StoredProcedures_OrganizationalAccess.sql"

# 4. Adaugă coloane ownership în Persoane, Users, Partners
Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "024_AddOwnershipColumns.sql"

# 5. (OPȚIONAL - DESTRUCTIV!) Cleanup date + setup admin
# ⚠️ ATENȚIE: Șterge TOATE datele exceptând admin@valyanerp.ro!
# Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "025_CleanupAndSetupAdmin.sql"

# 6. Update stored procedures cu parametri ownership
Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "026_UpdateStoredProcedures_WithOwnership.sql"

# 7. Enhancements: Cache, EntitySharing, Views filtrate, Trigger audit
Invoke-Sqlcmd -ServerInstance "TS1828\ERP" -Database "ValyanERP" -InputFile "027_OrganizationalAccess_Enhancements.sql"
```

### Rezumat Scripturi:

| Script | Descriere | Destructiv |
|--------|-----------|------------|
| `022_UserOrganizationalAccess.sql` | Tabel principal + Funcții TVF | ❌ |
| `023_StoredProcedures_OrganizationalAccess.sql` | CRUD permisiuni, Bulk, Copy, Tree | ❌ |
| `024_AddOwnershipColumns.sql` | ALTER Persoane/Users/Partners | ❌ |
| `025_CleanupAndSetupAdmin.sql` | Cleanup complet + seed admin | ⚠️ DA |
| `026_UpdateStoredProcedures_WithOwnership.sql` | SP-uri cu ownership params | ❌ |
| `027_OrganizationalAccess_Enhancements.sql` | Cache, Sharing, Views, Trigger | ❌ |

---

## ✅ Decizii Arhitecturale

| Întrebare | Decizie | Motivație |
|-----------|---------|-----------|
| Admin Override | ✅ Da | `HasFullAccess = true` pentru rolul Admin |
| Acces Negativ (Deny) | ✅ Da | `AccessLevel = 0` pentru excludere explicită |
| Valabilitate Temporară | ✅ Da | `ValidFrom/ValidTo` pentru contractori |
| Multi-Grup Access | ✅ Da | Un user poate avea acces la grupuri diferite |
| Cache Strategy | ✅ IMemoryCache | 30 min TTL, invalidare la modificare |
| Filtrare Primară | ✅ SESSION_CONTEXT + Views | Performanță optimă, securitate la nivel SQL |
| ORM | ✅ Dapper | Conform cerință, control complet SQL |

---

**Status Document:** ✅ FAZA 1 COMPLETĂ (SQL Scripts)  
**Versiune:** 2.1  
**Următorul Pas:** FAZA 2 - Implementare Backend C# (Dapper)
