-- =============================================
-- ValyanERP - Stored Procedures pentru User Organizational Access
-- Script: 023_StoredProcedures_OrganizationalAccess.sql
-- Data: 12 Ianuarie 2026
-- Descriere: CRUD și operații pentru sistemul de permisiuni organizaționale
-- =============================================

USE [ValyanERP];
GO

PRINT '=== Începere migrare 023_StoredProcedures_OrganizationalAccess ===';
GO

-- =============================================
-- 1. GET ALL: Returnează toate permisiunile unui user
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_GetByUser', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_GetByUser;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_GetByUser]
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        uoa.Id,
        uoa.UserId,
        uoa.EntityType,
        uoa.EntityId,
        CASE uoa.EntityType
            WHEN 'GROUP' THEN g.Denumire
            WHEN 'COMPANY' THEN c.Denumire + ' (' + c.CUI + ')'
            WHEN 'WORKPLACE' THEN wp.Denumire
            WHEN 'LOCATION' THEN l.Denumire + ' (' + l.CodIntern + ')'
        END AS EntityName,
        uoa.AccessLevel,
        uoa.InheritToChildren,
        uoa.ValidFrom,
        uoa.ValidTo,
        uoa.Notes,
        uoa.IsActive,
        uoa.CreatedAt,
        uoa.CreatedBy,
        uoa.UpdatedAt,
        uoa.UpdatedBy
    FROM UserOrganizationalAccess uoa
    LEFT JOIN CompanyGroups g ON uoa.EntityType = 'GROUP' AND g.Id = uoa.EntityId
    LEFT JOIN Companies c ON uoa.EntityType = 'COMPANY' AND c.Id = uoa.EntityId
    LEFT JOIN WorkPlaces wp ON uoa.EntityType = 'WORKPLACE' AND wp.Id = uoa.EntityId
    LEFT JOIN Locations l ON uoa.EntityType = 'LOCATION' AND l.Id = uoa.EntityId
    WHERE uoa.UserId = @UserId
    ORDER BY 
        CASE uoa.EntityType 
            WHEN 'GROUP' THEN 1 
            WHEN 'COMPANY' THEN 2 
            WHEN 'WORKPLACE' THEN 3 
            WHEN 'LOCATION' THEN 4 
        END,
        EntityName;
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_GetByUser creat';
GO

-- =============================================
-- 2. GET BY ID: Returnează o permisiune specifică
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_GetById', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_GetById;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_GetById]
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        uoa.Id,
        uoa.UserId,
        uoa.EntityType,
        uoa.EntityId,
        CASE uoa.EntityType
            WHEN 'GROUP' THEN g.Denumire
            WHEN 'COMPANY' THEN c.Denumire + ' (' + c.CUI + ')'
            WHEN 'WORKPLACE' THEN wp.Denumire
            WHEN 'LOCATION' THEN l.Denumire + ' (' + l.CodIntern + ')'
        END AS EntityName,
        uoa.AccessLevel,
        uoa.InheritToChildren,
        uoa.ValidFrom,
        uoa.ValidTo,
        uoa.Notes,
        uoa.IsActive,
        uoa.CreatedAt,
        uoa.CreatedBy,
        uoa.UpdatedAt,
        uoa.UpdatedBy
    FROM UserOrganizationalAccess uoa
    LEFT JOIN CompanyGroups g ON uoa.EntityType = 'GROUP' AND g.Id = uoa.EntityId
    LEFT JOIN Companies c ON uoa.EntityType = 'COMPANY' AND c.Id = uoa.EntityId
    LEFT JOIN WorkPlaces wp ON uoa.EntityType = 'WORKPLACE' AND wp.Id = uoa.EntityId
    LEFT JOIN Locations l ON uoa.EntityType = 'LOCATION' AND l.Id = uoa.EntityId
    WHERE uoa.Id = @Id;
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_GetById creat';
GO

-- =============================================
-- 3. CREATE: Adaugă o nouă permisiune
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_Create', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_Create;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_Create]
    @UserId UNIQUEIDENTIFIER,
    @EntityType VARCHAR(20),
    @EntityId UNIQUEIDENTIFIER,
    @AccessLevel TINYINT = 1,
    @InheritToChildren BIT = 1,
    @ValidFrom DATETIME2 = NULL,
    @ValidTo DATETIME2 = NULL,
    @Notes NVARCHAR(500) = NULL,
    @CreatedBy UNIQUEIDENTIFIER = NULL,
    @NewId UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @ErrorMessage NVARCHAR(500);
    
    -- Validare: EntityType valid
    IF @EntityType NOT IN ('GROUP', 'COMPANY', 'WORKPLACE', 'LOCATION')
    BEGIN
        SET @ErrorMessage = 'EntityType invalid. Valori acceptate: GROUP, COMPANY, WORKPLACE, LOCATION';
        THROW 50001, @ErrorMessage, 1;
    END
    
    -- Validare: Entitatea există
    IF @EntityType = 'GROUP' AND NOT EXISTS (SELECT 1 FROM CompanyGroups WHERE Id = @EntityId)
    BEGIN
        SET @ErrorMessage = 'Grupul specificat nu există.';
        THROW 50002, @ErrorMessage, 1;
    END
    
    IF @EntityType = 'COMPANY' AND NOT EXISTS (SELECT 1 FROM Companies WHERE Id = @EntityId)
    BEGIN
        SET @ErrorMessage = 'Compania specificată nu există.';
        THROW 50002, @ErrorMessage, 1;
    END
    
    IF @EntityType = 'WORKPLACE' AND NOT EXISTS (SELECT 1 FROM WorkPlaces WHERE Id = @EntityId)
    BEGIN
        SET @ErrorMessage = 'Punctul de lucru specificat nu există.';
        THROW 50002, @ErrorMessage, 1;
    END
    
    IF @EntityType = 'LOCATION' AND NOT EXISTS (SELECT 1 FROM Locations WHERE Id = @EntityId)
    BEGIN
        SET @ErrorMessage = 'Locația specificată nu există.';
        THROW 50002, @ErrorMessage, 1;
    END
    
    -- Validare: User-ul există
    IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = @UserId)
    BEGIN
        SET @ErrorMessage = 'Utilizatorul specificat nu există.';
        THROW 50003, @ErrorMessage, 1;
    END
    
    -- Validare: Nu există deja o permisiune activă pentru aceeași entitate
    IF EXISTS (
        SELECT 1 FROM UserOrganizationalAccess 
        WHERE UserId = @UserId 
        AND EntityType = @EntityType 
        AND EntityId = @EntityId 
        AND IsActive = 1
    )
    BEGIN
        SET @ErrorMessage = 'Există deja o permisiune activă pentru această entitate.';
        THROW 50004, @ErrorMessage, 1;
    END
    
    -- Validare: ValidTo >= ValidFrom
    IF @ValidFrom IS NOT NULL AND @ValidTo IS NOT NULL AND @ValidTo < @ValidFrom
    BEGIN
        SET @ErrorMessage = 'Data de sfârșit trebuie să fie după data de început.';
        THROW 50005, @ErrorMessage, 1;
    END
    
    SET @NewId = NEWID();
    
    INSERT INTO UserOrganizationalAccess (
        Id, UserId, EntityType, EntityId, AccessLevel, InheritToChildren,
        ValidFrom, ValidTo, Notes, IsActive, CreatedAt, CreatedBy
    )
    VALUES (
        @NewId, @UserId, @EntityType, @EntityId, @AccessLevel, @InheritToChildren,
        @ValidFrom, @ValidTo, @Notes, 1, GETDATE(), @CreatedBy
    );
    
    SELECT @NewId AS NewId;
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_Create creat';
GO

-- =============================================
-- 4. UPDATE: Modifică o permisiune existentă
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_Update', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_Update;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_Update]
    @Id UNIQUEIDENTIFIER,
    @AccessLevel TINYINT = NULL,
    @InheritToChildren BIT = NULL,
    @ValidFrom DATETIME2 = NULL,
    @ValidTo DATETIME2 = NULL,
    @Notes NVARCHAR(500) = NULL,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @ErrorMessage NVARCHAR(500);
    
    -- Validare: Permisiunea există
    IF NOT EXISTS (SELECT 1 FROM UserOrganizationalAccess WHERE Id = @Id)
    BEGIN
        SET @ErrorMessage = 'Permisiunea specificată nu există.';
        THROW 50001, @ErrorMessage, 1;
    END
    
    UPDATE UserOrganizationalAccess SET
        AccessLevel = COALESCE(@AccessLevel, AccessLevel),
        InheritToChildren = COALESCE(@InheritToChildren, InheritToChildren),
        ValidFrom = CASE WHEN @ValidFrom IS NOT NULL THEN @ValidFrom ELSE ValidFrom END,
        ValidTo = CASE WHEN @ValidTo IS NOT NULL THEN @ValidTo ELSE ValidTo END,
        Notes = CASE WHEN @Notes IS NOT NULL THEN @Notes ELSE Notes END,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_Update creat';
GO

-- =============================================
-- 5. DELETE (Soft): Dezactivează o permisiune
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_Delete', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_Delete;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_Delete]
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE UserOrganizationalAccess SET
        IsActive = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @UpdatedBy
    WHERE Id = @Id;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_Delete creat';
GO

-- =============================================
-- 6. BULK CREATE: Adaugă multiple permisiuni pentru un user
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_BulkCreate', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_BulkCreate;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_BulkCreate]
    @UserId UNIQUEIDENTIFIER,
    @AccessJson NVARCHAR(MAX), -- JSON array: [{"EntityType":"COMPANY","EntityId":"guid","AccessLevel":1}]
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Dezactivează permisiunile existente care nu sunt în noul set
    UPDATE uoa SET
        IsActive = 0,
        UpdatedAt = GETDATE(),
        UpdatedBy = @CreatedBy
    FROM UserOrganizationalAccess uoa
    WHERE uoa.UserId = @UserId
    AND uoa.IsActive = 1
    AND NOT EXISTS (
        SELECT 1 FROM OPENJSON(@AccessJson)
        WITH (
            EntityType VARCHAR(20) '$.EntityType',
            EntityId UNIQUEIDENTIFIER '$.EntityId'
        ) j
        WHERE j.EntityType = uoa.EntityType AND j.EntityId = uoa.EntityId
    );
    
    -- Inserează sau actualizează permisiunile noi
    MERGE UserOrganizationalAccess AS target
    USING (
        SELECT 
            EntityType,
            EntityId,
            COALESCE(AccessLevel, 1) AS AccessLevel,
            COALESCE(InheritToChildren, 1) AS InheritToChildren,
            ValidFrom,
            ValidTo,
            Notes
        FROM OPENJSON(@AccessJson)
        WITH (
            EntityType VARCHAR(20) '$.EntityType',
            EntityId UNIQUEIDENTIFIER '$.EntityId',
            AccessLevel TINYINT '$.AccessLevel',
            InheritToChildren BIT '$.InheritToChildren',
            ValidFrom DATETIME2 '$.ValidFrom',
            ValidTo DATETIME2 '$.ValidTo',
            Notes NVARCHAR(500) '$.Notes'
        )
    ) AS source
    ON target.UserId = @UserId 
       AND target.EntityType = source.EntityType 
       AND target.EntityId = source.EntityId
    
    WHEN MATCHED THEN
        UPDATE SET
            AccessLevel = source.AccessLevel,
            InheritToChildren = source.InheritToChildren,
            ValidFrom = source.ValidFrom,
            ValidTo = source.ValidTo,
            Notes = source.Notes,
            IsActive = 1,
            UpdatedAt = GETDATE(),
            UpdatedBy = @CreatedBy
    
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (UserId, EntityType, EntityId, AccessLevel, InheritToChildren, ValidFrom, ValidTo, Notes, IsActive, CreatedAt, CreatedBy)
        VALUES (@UserId, source.EntityType, source.EntityId, source.AccessLevel, source.InheritToChildren, source.ValidFrom, source.ValidTo, source.Notes, 1, GETDATE(), @CreatedBy);
    
    SELECT @@ROWCOUNT AS RowsAffected;
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_BulkCreate creat';
GO

-- =============================================
-- 7. GET PERIMETER: Calculează perimetrul complet pentru un user
-- Returnează toate ID-urile vizibile per nivel
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_GetPerimeter', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_GetPerimeter;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_GetPerimeter]
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Returnează 4 result sets: Groups, Companies, WorkPlaces, Locations
    
    -- 1. Grupuri vizibile
    SELECT GroupId, AccessLevel FROM dbo.fn_GetUserVisibleGroups(@UserId);
    
    -- 2. Companii vizibile
    SELECT CompanyId, AccessLevel FROM dbo.fn_GetUserVisibleCompanies(@UserId);
    
    -- 3. Puncte de lucru vizibile
    SELECT WorkPlaceId, AccessLevel FROM dbo.fn_GetUserVisibleWorkPlaces(@UserId);
    
    -- 4. Locații vizibile
    SELECT LocationId, AccessLevel FROM dbo.fn_GetUserVisibleLocations(@UserId);
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_GetPerimeter creat';
GO

-- =============================================
-- 8. GET PERIMETER JSON: Returnează perimetrul ca JSON (pentru cache în Session)
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_GetPerimeterJson', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_GetPerimeterJson;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_GetPerimeterJson]
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT (
        SELECT
            (SELECT GroupId, AccessLevel FROM dbo.fn_GetUserVisibleGroups(@UserId) FOR JSON PATH) AS VisibleGroups,
            (SELECT CompanyId, AccessLevel FROM dbo.fn_GetUserVisibleCompanies(@UserId) FOR JSON PATH) AS VisibleCompanies,
            (SELECT WorkPlaceId, AccessLevel FROM dbo.fn_GetUserVisibleWorkPlaces(@UserId) FOR JSON PATH) AS VisibleWorkPlaces,
            (SELECT LocationId, AccessLevel FROM dbo.fn_GetUserVisibleLocations(@UserId) FOR JSON PATH) AS VisibleLocations
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
    ) AS PerimeterJson;
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_GetPerimeterJson creat';
GO

-- =============================================
-- 9. UPDATE SESSION PERIMETER: Actualizează cache-ul din sesiune
-- =============================================
IF OBJECT_ID('dbo.sp_Sessions_UpdatePerimeter', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_Sessions_UpdatePerimeter;
GO

CREATE PROCEDURE [dbo].[sp_Sessions_UpdatePerimeter]
    @SessionId UNIQUEIDENTIFIER = NULL,
    @UserId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Dacă avem SessionId, actualizăm doar acea sesiune
    IF @SessionId IS NOT NULL
    BEGIN
        DECLARE @SessionUserId UNIQUEIDENTIFIER;
        SELECT @SessionUserId = UserId FROM Sessions WHERE Id = @SessionId AND IsActive = 1;
        
        IF @SessionUserId IS NOT NULL
        BEGIN
            DECLARE @PerimeterJson NVARCHAR(MAX);
            EXEC sp_UserOrganizationalAccess_GetPerimeterJson @SessionUserId;
            -- Rezultatul trebuie capturat de client și actualizat
        END
    END
    
    -- Dacă avem UserId, actualizăm toate sesiunile active ale user-ului
    IF @UserId IS NOT NULL
    BEGIN
        -- Invalidăm cache-ul existent (setăm PerimeterCalculatedAt = NULL)
        UPDATE Sessions SET
            UserPerimeterJson = NULL,
            PerimeterCalculatedAt = NULL
        WHERE UserId = @UserId AND IsActive = 1;
        
        SELECT @@ROWCOUNT AS SessionsInvalidated;
    END
END;
GO

PRINT '✅ SP sp_Sessions_UpdatePerimeter creat';
GO

-- =============================================
-- 10. GET USERS WITH ACCESS: Returnează toți userii cu acces la o entitate
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_GetUsersWithAccess', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_GetUsersWithAccess;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_GetUsersWithAccess]
    @EntityType VARCHAR(20),
    @EntityId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT DISTINCT
        u.Id AS UserId,
        u.UserName,
        u.Email,
        u.FirstName,
        u.LastName,
        uoa.AccessLevel,
        CASE 
            WHEN uoa.EntityType = @EntityType AND uoa.EntityId = @EntityId THEN 'Direct'
            ELSE 'Inherited'
        END AS AccessType,
        uoa.EntityType AS GrantedAtLevel,
        uoa.EntityId AS GrantedAtEntityId
    FROM Users u
    INNER JOIN UserOrganizationalAccess uoa ON uoa.UserId = u.Id AND uoa.IsActive = 1 AND uoa.AccessLevel > 0
    WHERE u.IsActive = 1
    AND (
        -- Acces direct la entitate
        (uoa.EntityType = @EntityType AND uoa.EntityId = @EntityId)
        OR
        -- Acces moștenit de la WORKPLACE (pentru LOCATION)
        (@EntityType = 'LOCATION' AND uoa.EntityType = 'WORKPLACE' AND uoa.InheritToChildren = 1
         AND EXISTS (SELECT 1 FROM Locations l WHERE l.Id = @EntityId AND l.WorkPlaceId = uoa.EntityId))
        OR
        -- Acces moștenit de la COMPANY (pentru LOCATION sau WORKPLACE)
        (@EntityType IN ('LOCATION', 'WORKPLACE') AND uoa.EntityType = 'COMPANY' AND uoa.InheritToChildren = 1
         AND (
             EXISTS (SELECT 1 FROM Locations l WHERE l.Id = @EntityId AND l.CompanyId = uoa.EntityId)
             OR EXISTS (SELECT 1 FROM WorkPlaces wp WHERE wp.Id = @EntityId AND wp.CompanyId = uoa.EntityId)
         ))
        OR
        -- Acces moștenit de la GROUP
        (uoa.EntityType = 'GROUP' AND uoa.InheritToChildren = 1
         AND EXISTS (
             SELECT 1 FROM Companies c 
             WHERE c.GroupId = uoa.EntityId 
             AND (
                 (@EntityType = 'COMPANY' AND c.Id = @EntityId)
                 OR (@EntityType = 'WORKPLACE' AND EXISTS (SELECT 1 FROM WorkPlaces wp WHERE wp.Id = @EntityId AND wp.CompanyId = c.Id))
                 OR (@EntityType = 'LOCATION' AND EXISTS (SELECT 1 FROM Locations l WHERE l.Id = @EntityId AND l.CompanyId = c.Id))
             )
         ))
    )
    ORDER BY u.LastName, u.FirstName;
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_GetUsersWithAccess creat';
GO

-- =============================================
-- 11. COPY ACCESS: Copiază permisiunile de la un user la altul
-- =============================================
IF OBJECT_ID('dbo.sp_UserOrganizationalAccess_CopyFromUser', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserOrganizationalAccess_CopyFromUser;
GO

CREATE PROCEDURE [dbo].[sp_UserOrganizationalAccess_CopyFromUser]
    @SourceUserId UNIQUEIDENTIFIER,
    @TargetUserId UNIQUEIDENTIFIER,
    @CreatedBy UNIQUEIDENTIFIER = NULL,
    @ReplaceExisting BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Validare
    IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = @SourceUserId)
    BEGIN
        THROW 50001, 'Utilizatorul sursă nu există.', 1;
    END
    
    IF NOT EXISTS (SELECT 1 FROM Users WHERE Id = @TargetUserId)
    BEGIN
        THROW 50002, 'Utilizatorul destinație nu există.', 1;
    END
    
    IF @SourceUserId = @TargetUserId
    BEGIN
        THROW 50003, 'Utilizatorul sursă și destinație nu pot fi identici.', 1;
    END
    
    -- Dacă ReplaceExisting, dezactivează permisiunile existente
    IF @ReplaceExisting = 1
    BEGIN
        UPDATE UserOrganizationalAccess SET
            IsActive = 0,
            UpdatedAt = GETDATE(),
            UpdatedBy = @CreatedBy
        WHERE UserId = @TargetUserId AND IsActive = 1;
    END
    
    -- Copiază permisiunile
    INSERT INTO UserOrganizationalAccess (
        UserId, EntityType, EntityId, AccessLevel, InheritToChildren,
        ValidFrom, ValidTo, Notes, IsActive, CreatedAt, CreatedBy
    )
    SELECT 
        @TargetUserId,
        EntityType,
        EntityId,
        AccessLevel,
        InheritToChildren,
        ValidFrom,
        ValidTo,
        'Copiat de la user ' + CAST(@SourceUserId AS NVARCHAR(50)),
        1,
        GETDATE(),
        @CreatedBy
    FROM UserOrganizationalAccess
    WHERE UserId = @SourceUserId 
    AND IsActive = 1
    AND NOT EXISTS (
        SELECT 1 FROM UserOrganizationalAccess uoa2
        WHERE uoa2.UserId = @TargetUserId
        AND uoa2.EntityType = UserOrganizationalAccess.EntityType
        AND uoa2.EntityId = UserOrganizationalAccess.EntityId
        AND uoa2.IsActive = 1
    );
    
    SELECT @@ROWCOUNT AS RowsCopied;
END;
GO

PRINT '✅ SP sp_UserOrganizationalAccess_CopyFromUser creat';
GO

-- =============================================
-- 12. GET ORGANIZATION TREE WITH USER ACCESS: 
-- Returnează arborele organizațional cu starea accesului pentru un user
-- =============================================
IF OBJECT_ID('dbo.sp_OrganizationTree_WithUserAccess', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_OrganizationTree_WithUserAccess;
GO

CREATE PROCEDURE [dbo].[sp_OrganizationTree_WithUserAccess]
    @UserId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        ot.NodeType,
        ot.NodeId,
        ot.ParentId,
        ot.Denumire,
        ot.Cod,
        ot.TipNode,
        ot.[Level],
        ot.SortOrder,
        -- Acces direct
        uoa.Id AS DirectAccessId,
        uoa.AccessLevel AS DirectAccessLevel,
        uoa.InheritToChildren,
        -- Acces efectiv (direct sau moștenit)
        CASE 
            WHEN uoa.Id IS NOT NULL THEN uoa.AccessLevel
            WHEN ot.NodeType = 'LOCATION' THEN (SELECT TOP 1 AccessLevel FROM dbo.fn_GetUserVisibleLocations(@UserId) WHERE LocationId = CAST(ot.NodeId AS UNIQUEIDENTIFIER))
            WHEN ot.NodeType = 'WORKPLACE' THEN (SELECT TOP 1 AccessLevel FROM dbo.fn_GetUserVisibleWorkPlaces(@UserId) WHERE WorkPlaceId = CAST(ot.NodeId AS UNIQUEIDENTIFIER))
            WHEN ot.NodeType = 'COMPANY' THEN (SELECT TOP 1 AccessLevel FROM dbo.fn_GetUserVisibleCompanies(@UserId) WHERE CompanyId = CAST(ot.NodeId AS UNIQUEIDENTIFIER))
            WHEN ot.NodeType = 'GROUP' THEN (SELECT TOP 1 AccessLevel FROM dbo.fn_GetUserVisibleGroups(@UserId) WHERE GroupId = CAST(ot.NodeId AS UNIQUEIDENTIFIER))
            ELSE NULL
        END AS EffectiveAccessLevel,
        CASE 
            WHEN uoa.Id IS NOT NULL THEN 'Direct'
            WHEN ot.NodeType = 'LOCATION' AND EXISTS (SELECT 1 FROM dbo.fn_GetUserVisibleLocations(@UserId) WHERE LocationId = CAST(ot.NodeId AS UNIQUEIDENTIFIER)) THEN 'Inherited'
            WHEN ot.NodeType = 'WORKPLACE' AND EXISTS (SELECT 1 FROM dbo.fn_GetUserVisibleWorkPlaces(@UserId) WHERE WorkPlaceId = CAST(ot.NodeId AS UNIQUEIDENTIFIER)) THEN 'Inherited'
            WHEN ot.NodeType = 'COMPANY' AND EXISTS (SELECT 1 FROM dbo.fn_GetUserVisibleCompanies(@UserId) WHERE CompanyId = CAST(ot.NodeId AS UNIQUEIDENTIFIER)) THEN 'Inherited'
            WHEN ot.NodeType = 'GROUP' AND EXISTS (SELECT 1 FROM dbo.fn_GetUserVisibleGroups(@UserId) WHERE GroupId = CAST(ot.NodeId AS UNIQUEIDENTIFIER)) THEN 'Inherited'
            ELSE 'NoAccess'
        END AS AccessType
    FROM vw_OrganizationTree ot
    LEFT JOIN UserOrganizationalAccess uoa 
        ON uoa.UserId = @UserId 
        AND uoa.EntityType = ot.NodeType 
        AND uoa.EntityId = CAST(ot.NodeId AS UNIQUEIDENTIFIER)
        AND uoa.IsActive = 1
    WHERE ot.IsActive = 1
    ORDER BY ot.[Level], ot.SortOrder, ot.Denumire;
END;
GO

PRINT '✅ SP sp_OrganizationTree_WithUserAccess creat';
GO

PRINT '=== Migrare 023_StoredProcedures_OrganizationalAccess completă ===';
GO
