-- =============================================
-- ValyanERP - Organizational Access Enhancements
-- Script: 027_OrganizationalAccess_Enhancements.sql
-- Data: 12 Ianuarie 2026
-- Descriere: Adaugă elemente suplimentare conform Design V2:
--   - UserVisibleEntitiesCache (cache performanță)
--   - EntitySharing (partajare entități între companii)
--   - AccessDeniedLog (audit încercări refuzate)
--   - GuidList TYPE (TVP pentru Dapper)
--   - Views filtrate cu SESSION_CONTEXT
--   - Trigger audit pe UserOrganizationalAccess
--   - SP pentru refresh cache
-- Dependințe: 022, 023, 024
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 027_OrganizationalAccess_Enhancements ===';
GO

-- =============================================
-- 1. TYPE: Lista de GUID-uri pentru Table-Valued Parameters (Dapper)
-- =============================================
IF NOT EXISTS (SELECT 1 FROM sys.types WHERE name = 'GuidList' AND is_table_type = 1)
BEGIN
    CREATE TYPE [dbo].[GuidList] AS TABLE (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
    );
    PRINT '✅ Type GuidList creat';
END
ELSE
BEGIN
    PRINT '⏭️ Type GuidList există deja';
END
GO

-- =============================================
-- 2. TABEL: UserVisibleEntitiesCache
-- Cache precalculat pentru performanță la filtrare
-- Refresh la modificare permisiuni sau periodic
-- =============================================
IF OBJECT_ID('dbo.UserVisibleEntitiesCache', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[UserVisibleEntitiesCache] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [UserId] UNIQUEIDENTIFIER NOT NULL,
        [CompanyId] UNIQUEIDENTIFIER NULL,
        [WorkPlaceId] UNIQUEIDENTIFIER NULL,
        [LocationId] UNIQUEIDENTIFIER NULL,
        [MaxAccessLevel] TINYINT NOT NULL DEFAULT 1,
        [CachedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        
        CONSTRAINT [PK_UserVisibleEntitiesCache] PRIMARY KEY CLUSTERED ([Id])
    );
    
    -- Indexuri pentru lookup rapid
    CREATE INDEX [IX_Cache_UserId] ON [UserVisibleEntitiesCache] ([UserId]);
    CREATE INDEX [IX_Cache_CompanyId] ON [UserVisibleEntitiesCache] ([CompanyId]) INCLUDE ([UserId], [MaxAccessLevel]);
    CREATE INDEX [IX_Cache_LocationId] ON [UserVisibleEntitiesCache] ([LocationId]) INCLUDE ([UserId], [MaxAccessLevel]);
    CREATE INDEX [IX_Cache_CachedAt] ON [UserVisibleEntitiesCache] ([CachedAt]);
    
    PRINT '✅ Tabel UserVisibleEntitiesCache creat cu indexuri';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel UserVisibleEntitiesCache există deja';
END
GO

-- =============================================
-- 3. TABEL: EntitySharing
-- Pentru entități partajate între companii (ex: Parteneri comuni)
-- =============================================
IF OBJECT_ID('dbo.EntitySharing', 'U') IS NULL
BEGIN
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
        [Notes] NVARCHAR(500) NULL,
        
        CONSTRAINT [FK_ES_OwnerCompany] FOREIGN KEY ([OwnerCompanyId]) REFERENCES [dbo].[Companies]([Id]),
        CONSTRAINT [FK_ES_SharedWithCompany] FOREIGN KEY ([SharedWithCompanyId]) REFERENCES [dbo].[Companies]([Id]),
        CONSTRAINT [FK_ES_SharedBy] FOREIGN KEY ([SharedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [CK_ES_AccessLevel] CHECK ([AccessLevel] BETWEEN 1 AND 2),
        CONSTRAINT [CK_ES_DifferentCompanies] CHECK ([OwnerCompanyId] <> [SharedWithCompanyId])
    );
    
    -- Indexuri
    CREATE UNIQUE INDEX [UQ_ES_Entity_SharedWith] 
        ON [EntitySharing] ([EntityType], [EntityId], [SharedWithCompanyId]) 
        WHERE [IsActive] = 1;
    CREATE INDEX [IX_ES_OwnerCompany] ON [EntitySharing] ([OwnerCompanyId], [EntityType]);
    CREATE INDEX [IX_ES_SharedWithCompany] ON [EntitySharing] ([SharedWithCompanyId], [EntityType]);
    CREATE INDEX [IX_ES_Entity] ON [EntitySharing] ([EntityType], [EntityId]);
    
    PRINT '✅ Tabel EntitySharing creat cu indexuri';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel EntitySharing există deja';
END
GO

-- =============================================
-- 4. TABEL: AccessDeniedLog
-- Log pentru încercări de acces refuzate (audit securitate)
-- =============================================
IF OBJECT_ID('dbo.AccessDeniedLog', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AccessDeniedLog] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [UserId] UNIQUEIDENTIFIER NOT NULL,
        [AttemptedEntityType] VARCHAR(50) NOT NULL,
        [AttemptedEntityId] UNIQUEIDENTIFIER NOT NULL,
        [RequestedAction] VARCHAR(50) NOT NULL,  -- 'Read', 'Write', 'Delete', 'Admin'
        [DeniedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
        [IpAddress] VARCHAR(45) NULL,
        [UserAgent] NVARCHAR(500) NULL,
        [RequestPath] NVARCHAR(500) NULL,
        [AdditionalInfo] NVARCHAR(MAX) NULL,
        
        CONSTRAINT [PK_AccessDeniedLog] PRIMARY KEY CLUSTERED ([Id]),
        CONSTRAINT [FK_ADL_User] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id])
    );
    
    -- Indexuri pentru analiză
    CREATE INDEX [IX_ADL_UserId] ON [AccessDeniedLog] ([UserId], [DeniedAt] DESC);
    CREATE INDEX [IX_ADL_Entity] ON [AccessDeniedLog] ([AttemptedEntityType], [AttemptedEntityId]);
    CREATE INDEX [IX_ADL_DeniedAt] ON [AccessDeniedLog] ([DeniedAt] DESC);
    
    PRINT '✅ Tabel AccessDeniedLog creat cu indexuri';
END
ELSE
BEGIN
    PRINT '⏭️ Tabel AccessDeniedLog există deja';
END
GO

-- =============================================
-- 5. STORED PROCEDURE: Refresh Cache pentru un User
-- Apelat când se modifică permisiunile
-- =============================================
IF OBJECT_ID('dbo.sp_RefreshUserVisibleEntitiesCache', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_RefreshUserVisibleEntitiesCache;
GO

CREATE PROCEDURE [dbo].[sp_RefreshUserVisibleEntitiesCache]
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        -- Șterge cache-ul vechi pentru acest user
        DELETE FROM UserVisibleEntitiesCache WHERE UserId = @UserId;
        
        -- Rebuild cache cu toate entitățile vizibile
        -- Inserăm câte o linie pentru fiecare locație vizibilă cu ierarhia completă
        INSERT INTO UserVisibleEntitiesCache (UserId, CompanyId, WorkPlaceId, LocationId, MaxAccessLevel, CachedAt)
        SELECT DISTINCT
            @UserId,
            l.CompanyId,
            l.WorkPlaceId,
            uvl.LocationId,
            uvl.AccessLevel AS MaxAccessLevel,
            GETDATE()
        FROM dbo.fn_GetUserVisibleLocations(@UserId) uvl
        INNER JOIN Locations l ON l.Id = uvl.LocationId AND l.IsActive = 1;
        
        -- Adaugă și companiile fără locații (dar cu acces)
        INSERT INTO UserVisibleEntitiesCache (UserId, CompanyId, WorkPlaceId, LocationId, MaxAccessLevel, CachedAt)
        SELECT DISTINCT
            @UserId,
            uvc.CompanyId,
            NULL,
            NULL,
            uvc.AccessLevel AS MaxAccessLevel,
            GETDATE()
        FROM dbo.fn_GetUserVisibleCompanies(@UserId) uvc
        WHERE NOT EXISTS (
            SELECT 1 FROM UserVisibleEntitiesCache cache 
            WHERE cache.UserId = @UserId AND cache.CompanyId = uvc.CompanyId
        );
        
        COMMIT TRANSACTION;
        
        SELECT @@ROWCOUNT AS CachedEntries;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

PRINT '✅ SP sp_RefreshUserVisibleEntitiesCache creat';
GO

-- =============================================
-- 6. STORED PROCEDURE: Refresh Cache pentru TOȚI userii
-- Poate fi rulat periodic (job)
-- =============================================
IF OBJECT_ID('dbo.sp_RefreshAllUserCaches', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_RefreshAllUserCaches;
GO

CREATE PROCEDURE [dbo].[sp_RefreshAllUserCaches]
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @UserId UNIQUEIDENTIFIER;
    DECLARE @ProcessedCount INT = 0;
    
    -- Curățăm tot cache-ul
    TRUNCATE TABLE UserVisibleEntitiesCache;
    
    -- Parcurgem toți userii activi cu permisiuni
    DECLARE user_cursor CURSOR FAST_FORWARD FOR
        SELECT DISTINCT UserId 
        FROM UserOrganizationalAccess 
        WHERE IsActive = 1;
    
    OPEN user_cursor;
    FETCH NEXT FROM user_cursor INTO @UserId;
    
    WHILE @@FETCH_STATUS = 0
    BEGIN
        EXEC sp_RefreshUserVisibleEntitiesCache @UserId;
        SET @ProcessedCount = @ProcessedCount + 1;
        FETCH NEXT FROM user_cursor INTO @UserId;
    END
    
    CLOSE user_cursor;
    DEALLOCATE user_cursor;
    
    SELECT @ProcessedCount AS UsersProcessed;
END;
GO

PRINT '✅ SP sp_RefreshAllUserCaches creat';
GO

-- =============================================
-- 7. STORED PROCEDURE: Log Access Denied
-- =============================================
IF OBJECT_ID('dbo.sp_LogAccessDenied', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_LogAccessDenied;
GO

CREATE PROCEDURE [dbo].[sp_LogAccessDenied]
    @UserId UNIQUEIDENTIFIER,
    @EntityType VARCHAR(50),
    @EntityId UNIQUEIDENTIFIER,
    @RequestedAction VARCHAR(50),
    @IpAddress VARCHAR(45) = NULL,
    @UserAgent NVARCHAR(500) = NULL,
    @RequestPath NVARCHAR(500) = NULL,
    @AdditionalInfo NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    INSERT INTO AccessDeniedLog (
        UserId, AttemptedEntityType, AttemptedEntityId, RequestedAction,
        IpAddress, UserAgent, RequestPath, AdditionalInfo
    )
    VALUES (
        @UserId, @EntityType, @EntityId, @RequestedAction,
        @IpAddress, @UserAgent, @RequestPath, @AdditionalInfo
    );
    
    SELECT SCOPE_IDENTITY() AS LogId;
END;
GO

PRINT '✅ SP sp_LogAccessDenied creat';
GO

-- =============================================
-- 8. STORED PROCEDURE: Check User Access (îmbunătățit)
-- Returnează AccessLevel sau 0 + logare opțională dacă refuzat
-- =============================================
IF OBJECT_ID('dbo.sp_CheckUserAccess', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_CheckUserAccess;
GO

CREATE PROCEDURE [dbo].[sp_CheckUserAccess]
    @UserId UNIQUEIDENTIFIER,
    @EntityType VARCHAR(20),
    @EntityId UNIQUEIDENTIFIER,
    @RequiredAccessLevel TINYINT = 1,  -- Default: Read
    @LogIfDenied BIT = 0,              -- Dacă true, logează accesul refuzat
    @IpAddress VARCHAR(45) = NULL,
    @RequestPath NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @ActualAccessLevel TINYINT = 0;
    DECLARE @IsAdmin BIT = 0;
    DECLARE @HasAccess BIT = 0;
    DECLARE @Reason NVARCHAR(200);
    
    -- Check dacă e admin (bypass total)
    IF EXISTS (
        SELECT 1 FROM UserRoles ur 
        INNER JOIN Roles r ON ur.RoleId = r.Id 
        WHERE ur.UserId = @UserId AND r.NormalizedName = 'ADMIN'
    )
    BEGIN
        SET @IsAdmin = 1;
        SET @ActualAccessLevel = 4;
        SET @HasAccess = 1;
        SET @Reason = 'Admin bypass - full access';
    END
    ELSE
    BEGIN
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
        
        -- Setează rezultatul
        IF @ActualAccessLevel >= @RequiredAccessLevel
        BEGIN
            SET @HasAccess = 1;
            SET @Reason = 'Access granted';
        END
        ELSE IF @ActualAccessLevel > 0
        BEGIN
            SET @HasAccess = 0;
            SET @Reason = 'Insufficient access level (has ' + CAST(@ActualAccessLevel AS VARCHAR) + ', needs ' + CAST(@RequiredAccessLevel AS VARCHAR) + ')';
        END
        ELSE
        BEGIN
            SET @HasAccess = 0;
            SET @Reason = 'No access to entity';
        END
    END
    
    -- Log dacă refuzat și s-a cerut logare
    IF @LogIfDenied = 1 AND @HasAccess = 0
    BEGIN
        EXEC sp_LogAccessDenied 
            @UserId = @UserId,
            @EntityType = @EntityType,
            @EntityId = @EntityId,
            @RequestedAction = 'CheckAccess',
            @IpAddress = @IpAddress,
            @RequestPath = @RequestPath,
            @AdditionalInfo = @Reason;
    END
    
    SELECT 
        @IsAdmin AS IsAdmin,
        @ActualAccessLevel AS AccessLevel,
        @HasAccess AS HasAccess,
        @Reason AS Reason;
END;
GO

PRINT '✅ SP sp_CheckUserAccess (enhanced) creat';
GO

-- =============================================
-- 9. STORED PROCEDURE: Entity Sharing CRUD
-- =============================================
IF OBJECT_ID('dbo.sp_EntitySharing_Create', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_EntitySharing_Create;
GO

CREATE PROCEDURE [dbo].[sp_EntitySharing_Create]
    @EntityType VARCHAR(50),
    @EntityId UNIQUEIDENTIFIER,
    @OwnerCompanyId UNIQUEIDENTIFIER,
    @SharedWithCompanyId UNIQUEIDENTIFIER,
    @AccessLevel TINYINT = 1,
    @SharedBy UNIQUEIDENTIFIER = NULL,
    @Notes NVARCHAR(500) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Validare: Nu poate partaja cu sine
    IF @OwnerCompanyId = @SharedWithCompanyId
    BEGIN
        THROW 50001, 'Nu se poate partaja o entitate cu aceeași companie care o deține.', 1;
    END
    
    -- Verifică dacă există deja
    IF EXISTS (
        SELECT 1 FROM EntitySharing 
        WHERE EntityType = @EntityType 
        AND EntityId = @EntityId 
        AND SharedWithCompanyId = @SharedWithCompanyId 
        AND IsActive = 1
    )
    BEGIN
        -- Update
        UPDATE EntitySharing SET
            AccessLevel = @AccessLevel,
            Notes = @Notes
        WHERE EntityType = @EntityType 
        AND EntityId = @EntityId 
        AND SharedWithCompanyId = @SharedWithCompanyId 
        AND IsActive = 1;
        
        SELECT 'Updated' AS Action;
    END
    ELSE
    BEGIN
        -- Insert
        INSERT INTO EntitySharing (EntityType, EntityId, OwnerCompanyId, SharedWithCompanyId, AccessLevel, SharedBy, Notes)
        VALUES (@EntityType, @EntityId, @OwnerCompanyId, @SharedWithCompanyId, @AccessLevel, @SharedBy, @Notes);
        
        SELECT 'Created' AS Action;
    END
END;
GO

PRINT '✅ SP sp_EntitySharing_Create creat';
GO

IF OBJECT_ID('dbo.sp_EntitySharing_Revoke', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_EntitySharing_Revoke;
GO

CREATE PROCEDURE [dbo].[sp_EntitySharing_Revoke]
    @EntityType VARCHAR(50),
    @EntityId UNIQUEIDENTIFIER,
    @SharedWithCompanyId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE EntitySharing SET IsActive = 0
    WHERE EntityType = @EntityType 
    AND EntityId = @EntityId 
    AND SharedWithCompanyId = @SharedWithCompanyId 
    AND IsActive = 1;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ SP sp_EntitySharing_Revoke creat';
GO

IF OBJECT_ID('dbo.sp_EntitySharing_GetByEntity', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_EntitySharing_GetByEntity;
GO

CREATE PROCEDURE [dbo].[sp_EntitySharing_GetByEntity]
    @EntityType VARCHAR(50),
    @EntityId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        es.Id,
        es.EntityType,
        es.EntityId,
        es.OwnerCompanyId,
        oc.Denumire AS OwnerCompanyName,
        es.SharedWithCompanyId,
        sc.Denumire AS SharedWithCompanyName,
        es.AccessLevel,
        es.SharedAt,
        es.SharedBy,
        u.FirstName + ' ' + u.LastName AS SharedByName,
        es.Notes
    FROM EntitySharing es
    INNER JOIN Companies oc ON oc.Id = es.OwnerCompanyId
    INNER JOIN Companies sc ON sc.Id = es.SharedWithCompanyId
    LEFT JOIN Users u ON u.Id = es.SharedBy
    WHERE es.EntityType = @EntityType 
    AND es.EntityId = @EntityId 
    AND es.IsActive = 1;
END;
GO

PRINT '✅ SP sp_EntitySharing_GetByEntity creat';
GO

-- =============================================
-- 10. VIEW FILTRAT: vw_Persoane_Filtered
-- Folosește SESSION_CONTEXT pentru filtrare automată
-- =============================================
IF OBJECT_ID('dbo.vw_Persoane_Filtered', 'V') IS NOT NULL
    DROP VIEW dbo.vw_Persoane_Filtered;
GO

CREATE VIEW [dbo].[vw_Persoane_Filtered]
AS
SELECT p.*
FROM Persoane p
WHERE p.IsActive = 1
AND (
    -- Admin vede tot
    CAST(SESSION_CONTEXT(N'IsAdmin') AS BIT) = 1
    OR
    -- Datele fără owner (legacy/globale) sunt vizibile pentru toți
    p.OwnerCompanyId IS NULL
    OR
    -- Sau datele din companiile la care are acces
    p.OwnerCompanyId IN (
        SELECT CompanyId 
        FROM dbo.fn_GetUserVisibleCompanies(
            CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
        )
    )
    OR
    -- Sau datele pe care le-a creat userul curent
    p.CreatedBy = CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
);
GO

PRINT '✅ View vw_Persoane_Filtered creat';
GO

-- =============================================
-- 11. VIEW FILTRAT: vw_Partners_Filtered
-- Include și logica de Entity Sharing
-- =============================================
IF OBJECT_ID('dbo.vw_Partners_Filtered', 'V') IS NOT NULL
    DROP VIEW dbo.vw_Partners_Filtered;
GO

CREATE VIEW [dbo].[vw_Partners_Filtered]
AS
SELECT p.*
FROM Partners p
WHERE p.IsActive = 1
AND (
    -- Admin vede tot
    CAST(SESSION_CONTEXT(N'IsAdmin') AS BIT) = 1
    OR
    -- Parteneri fără owner (legacy/globali)
    p.OwnerCompanyId IS NULL
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
    OR
    -- Sau datele pe care le-a creat userul curent
    p.CreatedBy = CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
);
GO

PRINT '✅ View vw_Partners_Filtered creat';
GO

-- =============================================
-- 12. VIEW FILTRAT: vw_Users_Filtered
-- =============================================
IF OBJECT_ID('dbo.vw_Users_Filtered', 'V') IS NOT NULL
    DROP VIEW dbo.vw_Users_Filtered;
GO

CREATE VIEW [dbo].[vw_Users_Filtered]
AS
SELECT u.*
FROM Users u
WHERE u.IsActive = 1
AND (
    -- Admin vede tot
    CAST(SESSION_CONTEXT(N'IsAdmin') AS BIT) = 1
    OR
    -- Useri fără owner (legacy/globali)
    u.OwnerCompanyId IS NULL
    OR
    -- Useri din companiile la care am acces
    u.OwnerCompanyId IN (
        SELECT CompanyId 
        FROM dbo.fn_GetUserVisibleCompanies(
            CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
        )
    )
    OR
    -- Userul curent se vede pe sine
    u.Id = CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)
);
GO

PRINT '✅ View vw_Users_Filtered creat';
GO

-- =============================================
-- 13. TRIGGER: Audit pe UserOrganizationalAccess
-- Logează toate modificările de permisiuni
-- =============================================
IF OBJECT_ID('dbo.TR_UOA_Audit', 'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_UOA_Audit;
GO

CREATE TRIGGER [dbo].[TR_UOA_Audit] 
ON [dbo].[UserOrganizationalAccess]
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Inserează în AuditLogs
    INSERT INTO AuditLogs (
        TableName, 
        RecordId, 
        Action, 
        OldValues, 
        NewValues, 
        UserId, 
        CreatedAt
    )
    SELECT 
        'UserOrganizationalAccess',
        COALESCE(i.Id, d.Id),
        CASE 
            WHEN i.Id IS NOT NULL AND d.Id IS NULL THEN 'INSERT'
            WHEN i.Id IS NOT NULL AND d.Id IS NOT NULL THEN 'UPDATE'
            ELSE 'DELETE'
        END,
        CASE WHEN d.Id IS NOT NULL THEN (
            SELECT d.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) ELSE NULL END,
        CASE WHEN i.Id IS NOT NULL THEN (
            SELECT i.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
        ) ELSE NULL END,
        COALESCE(i.UpdatedBy, i.CreatedBy, d.UpdatedBy, 
            TRY_CAST(SESSION_CONTEXT(N'UserId') AS UNIQUEIDENTIFIER)),
        GETDATE()
    FROM inserted i
    FULL OUTER JOIN deleted d ON i.Id = d.Id;
    
    -- Invalidează cache-ul pentru userii afectați
    -- (Opțional: poate fi făcut sincron sau prin job)
    DECLARE @AffectedUsers TABLE (UserId UNIQUEIDENTIFIER);
    
    INSERT INTO @AffectedUsers (UserId)
    SELECT DISTINCT UserId FROM inserted
    UNION
    SELECT DISTINCT UserId FROM deleted;
    
    -- Ștergem cache-ul pentru userii afectați
    DELETE cache
    FROM UserVisibleEntitiesCache cache
    INNER JOIN @AffectedUsers au ON au.UserId = cache.UserId;
END;
GO

PRINT '✅ Trigger TR_UOA_Audit creat';
GO

-- =============================================
-- 14. FUNCȚIE: Verifică rapid dacă user are acces la entitate (optimizat pentru cache)
-- =============================================
IF OBJECT_ID('dbo.fn_UserHasAccessFromCache', 'FN') IS NOT NULL
    DROP FUNCTION dbo.fn_UserHasAccessFromCache;
GO

CREATE FUNCTION dbo.fn_UserHasAccessFromCache(
    @UserId UNIQUEIDENTIFIER,
    @CompanyId UNIQUEIDENTIFIER,
    @RequiredAccessLevel TINYINT = 1
)
RETURNS BIT
AS
BEGIN
    DECLARE @HasAccess BIT = 0;
    
    -- Verifică în cache
    IF EXISTS (
        SELECT 1 FROM UserVisibleEntitiesCache
        WHERE UserId = @UserId 
        AND CompanyId = @CompanyId 
        AND MaxAccessLevel >= @RequiredAccessLevel
    )
        SET @HasAccess = 1;
    
    RETURN @HasAccess;
END;
GO

PRINT '✅ Funcție fn_UserHasAccessFromCache creată';
GO

-- =============================================
-- 15. JOB HELPER: Curățare periodică AccessDeniedLog (păstrează 90 zile)
-- =============================================
IF OBJECT_ID('dbo.sp_CleanupAccessDeniedLog', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_CleanupAccessDeniedLog;
GO

CREATE PROCEDURE [dbo].[sp_CleanupAccessDeniedLog]
    @RetentionDays INT = 90
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@RetentionDays, GETDATE());
    
    DELETE FROM AccessDeniedLog WHERE DeniedAt < @CutoffDate;
    
    SELECT @@ROWCOUNT AS RowsDeleted;
END;
GO

PRINT '✅ SP sp_CleanupAccessDeniedLog creat';
GO

-- =============================================
-- 16. Statistici și rapoarte
-- =============================================
IF OBJECT_ID('dbo.sp_GetAccessStatistics', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_GetAccessStatistics;
GO

CREATE PROCEDURE [dbo].[sp_GetAccessStatistics]
    @StartDate DATETIME2 = NULL,
    @EndDate DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    SET @StartDate = COALESCE(@StartDate, DATEADD(DAY, -30, GETDATE()));
    SET @EndDate = COALESCE(@EndDate, GETDATE());
    
    -- 1. Total useri cu permisiuni
    SELECT 
        (SELECT COUNT(DISTINCT UserId) FROM UserOrganizationalAccess WHERE IsActive = 1) AS UsersWithPermissions,
        (SELECT COUNT(*) FROM UserOrganizationalAccess WHERE IsActive = 1) AS TotalActivePermissions,
        (SELECT COUNT(*) FROM EntitySharing WHERE IsActive = 1) AS TotalSharedEntities,
        (SELECT COUNT(*) FROM AccessDeniedLog WHERE DeniedAt BETWEEN @StartDate AND @EndDate) AS AccessDeniedCount,
        (SELECT COUNT(*) FROM UserVisibleEntitiesCache) AS CachedEntries;
    
    -- 2. Top useri cu acces refuzat
    SELECT TOP 10
        u.Email,
        u.FirstName + ' ' + u.LastName AS UserName,
        COUNT(*) AS DeniedCount
    FROM AccessDeniedLog adl
    INNER JOIN Users u ON u.Id = adl.UserId
    WHERE adl.DeniedAt BETWEEN @StartDate AND @EndDate
    GROUP BY u.Email, u.FirstName, u.LastName
    ORDER BY DeniedCount DESC;
    
    -- 3. Distribuție permisiuni pe tip entitate
    SELECT 
        EntityType,
        COUNT(*) AS PermissionCount,
        COUNT(DISTINCT UserId) AS UniqueUsers
    FROM UserOrganizationalAccess
    WHERE IsActive = 1
    GROUP BY EntityType
    ORDER BY PermissionCount DESC;
END;
GO

PRINT '✅ SP sp_GetAccessStatistics creat';
GO

PRINT '=== Migrare 027_OrganizationalAccess_Enhancements completă ===';
GO
