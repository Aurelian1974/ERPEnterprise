-- =============================================
-- ValyanERP - User Organizational Access
-- Script: 022_UserOrganizationalAccess.sql
-- Data: 12 Ianuarie 2026
-- Descriere: Sistem de permisiuni organizaționale granulare
--            Permite acces la orice nivel al ierarhiei:
--            GRUP → SOCIETATE → PUNCT LUCRU → LOCAȚIE
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 022_UserOrganizationalAccess ===';
GO

-- =============================================
-- 1. TABEL PRINCIPAL: UserOrganizationalAccess
-- =============================================
IF OBJECT_ID('dbo.UserOrganizationalAccess', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserOrganizationalAccess] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        [UserId] UNIQUEIDENTIFIER NOT NULL,
        
        -- Tipul entității la care are acces
        [EntityType] VARCHAR(20) NOT NULL, -- 'GROUP', 'COMPANY', 'WORKPLACE', 'LOCATION'
        [EntityId] UNIQUEIDENTIFIER NOT NULL,
        
        -- Nivel de acces
        [AccessLevel] TINYINT NOT NULL DEFAULT 1, 
        -- 0 = NoAccess (explicit denied)
        -- 1 = Read (doar vizualizare)
        -- 2 = Write (creare, modificare)
        -- 3 = Full (include ștergere)
        -- 4 = Admin (poate administra access-ul altora)
        
        -- Moștenire către entități copil
        [InheritToChildren] BIT NOT NULL DEFAULT 1,
        -- TRUE = accesul se propagă la toate entitățile subordonate
        -- FALSE = acces DOAR la această entitate, nu și la copii
        
        -- Valabilitate (opțional, pentru acces temporar)
        [ValidFrom] DATETIME2 NULL,
        [ValidTo] DATETIME2 NULL,
        
        -- Note explicative
        [Notes] NVARCHAR(500) NULL,
        
        -- Audit
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Constraints
        CONSTRAINT [FK_UOA_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UOA_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_UOA_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [CK_UOA_EntityType] CHECK ([EntityType] IN ('GROUP', 'COMPANY', 'WORKPLACE', 'LOCATION')),
        CONSTRAINT [CK_UOA_AccessLevel] CHECK ([AccessLevel] BETWEEN 0 AND 4),
        CONSTRAINT [CK_UOA_ValidDates] CHECK ([ValidTo] IS NULL OR [ValidFrom] IS NULL OR [ValidTo] >= [ValidFrom])
    );
    
    PRINT '✅ Tabel UserOrganizationalAccess creat cu succes';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel UserOrganizationalAccess există deja';
END
GO

-- =============================================
-- 2. INDEX UNIC: Un user nu poate avea duplicate pentru aceeași entitate
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_UOA_User_Entity')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_UOA_User_Entity] 
    ON [UserOrganizationalAccess] ([UserId], [EntityType], [EntityId]) 
    WHERE [IsActive] = 1;
    PRINT '✅ Index UQ_UOA_User_Entity creat';
END
GO

-- =============================================
-- 3. INDEXURI PENTRU PERFORMANCE
-- =============================================

-- Index pentru căutare după user
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UOA_UserId')
BEGIN
    CREATE INDEX [IX_UOA_UserId] 
    ON [UserOrganizationalAccess] ([UserId]) 
    INCLUDE ([EntityType], [EntityId], [AccessLevel], [InheritToChildren]);
    PRINT '✅ Index IX_UOA_UserId creat';
END
GO

-- Index pentru căutare după entitate
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UOA_EntityType_EntityId')
BEGIN
    CREATE INDEX [IX_UOA_EntityType_EntityId] 
    ON [UserOrganizationalAccess] ([EntityType], [EntityId]) 
    INCLUDE ([UserId], [AccessLevel]);
    PRINT '✅ Index IX_UOA_EntityType_EntityId creat';
END
GO

-- Index pentru validitate temporală
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UOA_ValidDates')
BEGIN
    CREATE INDEX [IX_UOA_ValidDates] 
    ON [UserOrganizationalAccess] ([ValidFrom], [ValidTo]) 
    WHERE [IsActive] = 1;
    PRINT '✅ Index IX_UOA_ValidDates creat';
END
GO

-- Index pentru IsActive
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_UOA_IsActive')
BEGIN
    CREATE INDEX [IX_UOA_IsActive] 
    ON [UserOrganizationalAccess] ([IsActive]) 
    INCLUDE ([UserId], [EntityType], [EntityId], [AccessLevel]);
    PRINT '✅ Index IX_UOA_IsActive creat';
END
GO

-- =============================================
-- 4. FUNCȚIE: Verifică dacă un acces este valid acum
-- =============================================
IF OBJECT_ID('dbo.fn_UOA_IsAccessValid', 'FN') IS NOT NULL
    DROP FUNCTION dbo.fn_UOA_IsAccessValid;
GO

CREATE FUNCTION dbo.fn_UOA_IsAccessValid(
    @IsActive BIT,
    @AccessLevel TINYINT,
    @ValidFrom DATETIME2,
    @ValidTo DATETIME2
)
RETURNS BIT
AS
BEGIN
    -- Verifică dacă accesul este valid în acest moment
    IF @IsActive = 0 OR @AccessLevel = 0
        RETURN 0;
    
    IF @ValidFrom IS NOT NULL AND @ValidFrom > GETDATE()
        RETURN 0;
    
    IF @ValidTo IS NOT NULL AND @ValidTo < GETDATE()
        RETURN 0;
    
    RETURN 1;
END;
GO

PRINT '✅ Funcție fn_UOA_IsAccessValid creată cu succes';
GO

-- =============================================
-- 5. FUNCȚIE TVF: Returnează toate LOCATION IDs vizibile pentru un user
-- Aceasta este "cheia" pentru filtrarea automată a datelor
-- =============================================
IF OBJECT_ID('dbo.fn_GetUserVisibleLocations', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_GetUserVisibleLocations;
GO

CREATE FUNCTION [dbo].[fn_GetUserVisibleLocations](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    -- 1. Locații cu acces DIRECT
    SELECT DISTINCT l.Id AS LocationId, uoa.AccessLevel
    FROM Locations l
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'LOCATION' 
        AND uoa.EntityId = l.Id
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND l.IsActive = 1
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 2. Locații din WORKPLACE cu acces (cu InheritToChildren)
    SELECT l.Id AS LocationId, uoa.AccessLevel
    FROM Locations l
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'WORKPLACE' 
        AND uoa.EntityId = l.WorkPlaceId
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND uoa.InheritToChildren = 1
      AND l.IsActive = 1
      AND l.WorkPlaceId IS NOT NULL
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 3. Locații din COMPANY cu acces (cu InheritToChildren)
    SELECT l.Id AS LocationId, uoa.AccessLevel
    FROM Locations l
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'COMPANY' 
        AND uoa.EntityId = l.CompanyId
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND uoa.InheritToChildren = 1
      AND l.IsActive = 1
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 4. Locații din GRUP cu acces (cu InheritToChildren)
    SELECT l.Id AS LocationId, uoa.AccessLevel
    FROM Locations l
    INNER JOIN Companies c ON c.Id = l.CompanyId AND c.IsActive = 1
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'GROUP' 
        AND uoa.EntityId = c.GroupId
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND uoa.InheritToChildren = 1
      AND l.IsActive = 1
      AND c.GroupId IS NOT NULL
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
);
GO

PRINT '✅ Funcție fn_GetUserVisibleLocations creată cu succes';
GO

-- =============================================
-- 6. FUNCȚIE TVF: Returnează toate WORKPLACE IDs vizibile pentru un user
-- =============================================
IF OBJECT_ID('dbo.fn_GetUserVisibleWorkPlaces', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_GetUserVisibleWorkPlaces;
GO

CREATE FUNCTION [dbo].[fn_GetUserVisibleWorkPlaces](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    -- 1. Puncte de lucru cu acces DIRECT
    SELECT DISTINCT wp.Id AS WorkPlaceId, uoa.AccessLevel
    FROM WorkPlaces wp
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'WORKPLACE' 
        AND uoa.EntityId = wp.Id
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND wp.IsActive = 1
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 2. Puncte de lucru din COMPANY cu acces (cu InheritToChildren)
    SELECT wp.Id AS WorkPlaceId, uoa.AccessLevel
    FROM WorkPlaces wp
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'COMPANY' 
        AND uoa.EntityId = wp.CompanyId
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND uoa.InheritToChildren = 1
      AND wp.IsActive = 1
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 3. Puncte de lucru din GRUP cu acces (cu InheritToChildren)
    SELECT wp.Id AS WorkPlaceId, uoa.AccessLevel
    FROM WorkPlaces wp
    INNER JOIN Companies c ON c.Id = wp.CompanyId AND c.IsActive = 1
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'GROUP' 
        AND uoa.EntityId = c.GroupId
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND uoa.InheritToChildren = 1
      AND wp.IsActive = 1
      AND c.GroupId IS NOT NULL
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
);
GO

PRINT '✅ Funcție fn_GetUserVisibleWorkPlaces creată cu succes';
GO

-- =============================================
-- 7. FUNCȚIE TVF: Returnează toate COMPANY IDs vizibile pentru un user
-- =============================================
IF OBJECT_ID('dbo.fn_GetUserVisibleCompanies', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_GetUserVisibleCompanies;
GO

CREATE FUNCTION [dbo].[fn_GetUserVisibleCompanies](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    -- 1. Companii cu acces DIRECT
    SELECT DISTINCT c.Id AS CompanyId, uoa.AccessLevel
    FROM Companies c
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'COMPANY' 
        AND uoa.EntityId = c.Id
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND c.IsActive = 1
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 2. Companii din GRUP cu acces (cu InheritToChildren)
    SELECT c.Id AS CompanyId, uoa.AccessLevel
    FROM Companies c
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'GROUP' 
        AND uoa.EntityId = c.GroupId
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND uoa.InheritToChildren = 1
      AND c.IsActive = 1
      AND c.GroupId IS NOT NULL
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 3. Companii la care userul are acces indirect (prin access la un WorkPlace sau Location din ele)
    SELECT DISTINCT c.Id AS CompanyId, MAX(uvl.AccessLevel) AS AccessLevel
    FROM Companies c
    INNER JOIN Locations l ON l.CompanyId = c.Id AND l.IsActive = 1
    INNER JOIN dbo.fn_GetUserVisibleLocations(@UserId) uvl ON uvl.LocationId = l.Id
    WHERE c.IsActive = 1
    GROUP BY c.Id
);
GO

PRINT '✅ Funcție fn_GetUserVisibleCompanies creată cu succes';
GO

-- =============================================
-- 8. FUNCȚIE TVF: Returnează toate GROUP IDs vizibile pentru un user
-- =============================================
IF OBJECT_ID('dbo.fn_GetUserVisibleGroups', 'IF') IS NOT NULL
    DROP FUNCTION dbo.fn_GetUserVisibleGroups;
GO

CREATE FUNCTION [dbo].[fn_GetUserVisibleGroups](@UserId UNIQUEIDENTIFIER)
RETURNS TABLE
AS
RETURN (
    -- 1. Grupuri cu acces DIRECT
    SELECT DISTINCT g.Id AS GroupId, uoa.AccessLevel
    FROM CompanyGroups g
    INNER JOIN UserOrganizationalAccess uoa 
        ON uoa.EntityType = 'GROUP' 
        AND uoa.EntityId = g.Id
    WHERE uoa.UserId = @UserId 
      AND uoa.IsActive = 1 
      AND uoa.AccessLevel > 0
      AND g.IsActive = 1
      AND (uoa.ValidFrom IS NULL OR uoa.ValidFrom <= GETDATE())
      AND (uoa.ValidTo IS NULL OR uoa.ValidTo >= GETDATE())
    
    UNION
    
    -- 2. Grupuri la care userul are acces indirect (prin access la o companie din ele)
    SELECT DISTINCT g.Id AS GroupId, MAX(uvc.AccessLevel) AS AccessLevel
    FROM CompanyGroups g
    INNER JOIN Companies c ON c.GroupId = g.Id AND c.IsActive = 1
    INNER JOIN dbo.fn_GetUserVisibleCompanies(@UserId) uvc ON uvc.CompanyId = c.Id
    WHERE g.IsActive = 1
    GROUP BY g.Id
);
GO

PRINT '✅ Funcție fn_GetUserVisibleGroups creată cu succes';
GO

-- =============================================
-- 9. VIEW: Perimetru complet pentru un user (pentru debugging/audit)
-- =============================================
IF OBJECT_ID('dbo.vw_UserOrganizationalPerimeter', 'V') IS NOT NULL
    DROP VIEW dbo.vw_UserOrganizationalPerimeter;
GO

CREATE VIEW [dbo].[vw_UserOrganizationalPerimeter] AS
SELECT 
    uoa.UserId,
    u.UserName,
    u.Email,
    uoa.EntityType,
    uoa.EntityId,
    CASE uoa.EntityType
        WHEN 'GROUP' THEN g.Denumire
        WHEN 'COMPANY' THEN c.Denumire + ' (' + c.CUI + ')'
        WHEN 'WORKPLACE' THEN wp.Denumire
        WHEN 'LOCATION' THEN l.Denumire + ' (' + l.CodIntern + ')'
    END AS EntityName,
    uoa.AccessLevel,
    CASE uoa.AccessLevel
        WHEN 0 THEN 'NoAccess'
        WHEN 1 THEN 'Read'
        WHEN 2 THEN 'Write'
        WHEN 3 THEN 'Full'
        WHEN 4 THEN 'Admin'
    END AS AccessLevelName,
    uoa.InheritToChildren,
    uoa.ValidFrom,
    uoa.ValidTo,
    CASE 
        WHEN uoa.IsActive = 0 THEN 'Inactive'
        WHEN uoa.ValidFrom IS NOT NULL AND uoa.ValidFrom > GETDATE() THEN 'Pending'
        WHEN uoa.ValidTo IS NOT NULL AND uoa.ValidTo < GETDATE() THEN 'Expired'
        ELSE 'Active'
    END AS Status,
    uoa.Notes,
    uoa.CreatedAt,
    uoa.CreatedBy,
    uoa.UpdatedAt,
    uoa.UpdatedBy
FROM UserOrganizationalAccess uoa
INNER JOIN Users u ON u.Id = uoa.UserId
LEFT JOIN CompanyGroups g ON uoa.EntityType = 'GROUP' AND g.Id = uoa.EntityId
LEFT JOIN Companies c ON uoa.EntityType = 'COMPANY' AND c.Id = uoa.EntityId
LEFT JOIN WorkPlaces wp ON uoa.EntityType = 'WORKPLACE' AND wp.Id = uoa.EntityId
LEFT JOIN Locations l ON uoa.EntityType = 'LOCATION' AND l.Id = uoa.EntityId;
GO

PRINT '✅ View vw_UserOrganizationalPerimeter creat cu succes';
GO

-- =============================================
-- 10. Adăugare coloană în Sessions pentru cache perimetru
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Sessions') AND name = 'UserPerimeterJson')
BEGIN
    ALTER TABLE [dbo].[Sessions] ADD
        [UserPerimeterJson] NVARCHAR(MAX) NULL,
        [PerimeterCalculatedAt] DATETIME2 NULL;
    PRINT '✅ Coloane UserPerimeterJson, PerimeterCalculatedAt adăugate în Sessions';
END
ELSE
BEGIN
    PRINT '⏭️ Coloanele perimetru există deja în Sessions';
END
GO

-- =============================================
-- 11. FUNCȚIE: Verifică dacă un user are acces la o entitate specifică
-- =============================================
IF OBJECT_ID('dbo.fn_UserHasAccessToEntity', 'FN') IS NOT NULL
    DROP FUNCTION dbo.fn_UserHasAccessToEntity;
GO

CREATE FUNCTION dbo.fn_UserHasAccessToEntity(
    @UserId UNIQUEIDENTIFIER,
    @EntityType VARCHAR(20),
    @EntityId UNIQUEIDENTIFIER,
    @RequiredAccessLevel TINYINT = 1
)
RETURNS BIT
AS
BEGIN
    DECLARE @HasAccess BIT = 0;
    
    IF @EntityType = 'LOCATION'
    BEGIN
        IF EXISTS (
            SELECT 1 FROM dbo.fn_GetUserVisibleLocations(@UserId) 
            WHERE LocationId = @EntityId AND AccessLevel >= @RequiredAccessLevel
        )
            SET @HasAccess = 1;
    END
    ELSE IF @EntityType = 'WORKPLACE'
    BEGIN
        IF EXISTS (
            SELECT 1 FROM dbo.fn_GetUserVisibleWorkPlaces(@UserId) 
            WHERE WorkPlaceId = @EntityId AND AccessLevel >= @RequiredAccessLevel
        )
            SET @HasAccess = 1;
    END
    ELSE IF @EntityType = 'COMPANY'
    BEGIN
        IF EXISTS (
            SELECT 1 FROM dbo.fn_GetUserVisibleCompanies(@UserId) 
            WHERE CompanyId = @EntityId AND AccessLevel >= @RequiredAccessLevel
        )
            SET @HasAccess = 1;
    END
    ELSE IF @EntityType = 'GROUP'
    BEGIN
        IF EXISTS (
            SELECT 1 FROM dbo.fn_GetUserVisibleGroups(@UserId) 
            WHERE GroupId = @EntityId AND AccessLevel >= @RequiredAccessLevel
        )
            SET @HasAccess = 1;
    END
    
    RETURN @HasAccess;
END;
GO

PRINT '✅ Funcție fn_UserHasAccessToEntity creată cu succes';
GO

-- =============================================
-- 12. SEED DATA: Acces complet pentru admin existent
-- =============================================
DECLARE @AdminUserId UNIQUEIDENTIFIER;
SELECT TOP 1 @AdminUserId = u.Id 
FROM Users u
INNER JOIN UserRoles ur ON ur.UserId = u.Id
INNER JOIN Roles r ON r.Id = ur.RoleId AND r.NormalizedName = 'ADMIN'
WHERE u.IsActive = 1;

IF @AdminUserId IS NOT NULL
BEGIN
    -- Verifică dacă există grupuri
    IF EXISTS (SELECT 1 FROM CompanyGroups WHERE IsActive = 1)
    BEGIN
        -- Dă acces la toate grupurile existente
        INSERT INTO UserOrganizationalAccess (UserId, EntityType, EntityId, AccessLevel, InheritToChildren, Notes, CreatedAt)
        SELECT 
            @AdminUserId, 
            'GROUP', 
            g.Id, 
            4, -- Admin
            1, -- Inherit to children
            'Auto-seed: Admin access la toate grupurile',
            GETDATE()
        FROM CompanyGroups g
        WHERE g.IsActive = 1
        AND NOT EXISTS (
            SELECT 1 FROM UserOrganizationalAccess uoa 
            WHERE uoa.UserId = @AdminUserId 
            AND uoa.EntityType = 'GROUP' 
            AND uoa.EntityId = g.Id
            AND uoa.IsActive = 1
        );
        
        PRINT '✅ Admin access seed data adăugat pentru grupuri';
    END
    ELSE IF EXISTS (SELECT 1 FROM Companies WHERE IsActive = 1)
    BEGIN
        -- Dacă nu există grupuri, dă acces la companii
        INSERT INTO UserOrganizationalAccess (UserId, EntityType, EntityId, AccessLevel, InheritToChildren, Notes, CreatedAt)
        SELECT 
            @AdminUserId, 
            'COMPANY', 
            c.Id, 
            4, -- Admin
            1, -- Inherit to children
            'Auto-seed: Admin access la toate companiile',
            GETDATE()
        FROM Companies c
        WHERE c.IsActive = 1
        AND NOT EXISTS (
            SELECT 1 FROM UserOrganizationalAccess uoa 
            WHERE uoa.UserId = @AdminUserId 
            AND uoa.EntityType = 'COMPANY' 
            AND uoa.EntityId = c.Id
            AND uoa.IsActive = 1
        );
        
        PRINT '✅ Admin access seed data adăugat pentru companii';
    END
    ELSE
    BEGIN
        PRINT '⚠️ Nu există grupuri sau companii pentru seed data';
    END
END
ELSE
BEGIN
    PRINT '⚠️ Nu s-a găsit user admin pentru seed data';
END
GO

PRINT '=== Migrare 022_UserOrganizationalAccess completă ===';
GO
