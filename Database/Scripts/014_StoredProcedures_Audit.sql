-- ================================================
-- Script: 014_StoredProcedures_Audit.sql
-- Description: Stored Procedures for Audit System
-- Author: Copilot + User
-- Date: 2026-01-09
-- Dependencies: 013_AuditSystem.sql
-- ================================================

USE [ValyanERP];
GO

PRINT '================================================';
PRINT 'Creating Audit System Stored Procedures';
PRINT '================================================';
PRINT '';

-- ================================================
-- SP 1: sp_AuditLogs_Create
-- Purpose: Insert new audit log with field-level details
-- ================================================
IF OBJECT_ID('dbo.sp_AuditLogs_Create', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_AuditLogs_Create;
GO

CREATE PROCEDURE dbo.sp_AuditLogs_Create
    @EntityType NVARCHAR(100),
    @EntityId NVARCHAR(450),
    @TableName NVARCHAR(100),
    @OperationType NVARCHAR(20),
    @UserId UNIQUEIDENTIFIER,
    @UserEmail NVARCHAR(256),
    @UserFullName NVARCHAR(200) = NULL,
    @SessionId UNIQUEIDENTIFIER = NULL,
    @IPAddress NVARCHAR(45) = NULL,
    @UserAgent NVARCHAR(500) = NULL,
    @Notes NVARCHAR(MAX) = NULL,
    @ChangedFields NVARCHAR(MAX) = NULL  -- JSON array: [{"FieldName":"X","OldValue":"A","NewValue":"B","DataType":"string","IsSensitive":false}]
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        DECLARE @AuditLogId UNIQUEIDENTIFIER = NEWID();
        DECLARE @ChangedFieldsCount INT = 0;
        
        -- Parse JSON to count fields
        IF @ChangedFields IS NOT NULL AND @ChangedFields != '[]'
        BEGIN
            SELECT @ChangedFieldsCount = COUNT(*)
            FROM OPENJSON(@ChangedFields);
        END
        
        -- Insert master audit log
        INSERT INTO [dbo].[AuditLogs] (
            [Id], [EntityType], [EntityId], [TableName], [OperationType],
            [UserId], [UserEmail], [UserFullName],
            [SessionId], [IPAddress], [UserAgent],
            [Timestamp], [ChangedFieldsCount], [Notes], [CreatedAt]
        ) VALUES (
            @AuditLogId, @EntityType, @EntityId, @TableName, @OperationType,
            @UserId, @UserEmail, @UserFullName,
            @SessionId, @IPAddress, @UserAgent,
            GETDATE(), @ChangedFieldsCount, @Notes, GETDATE()
        );
        
        -- Insert field-level details (if any)
        IF @ChangedFields IS NOT NULL AND @ChangedFields != '[]'
        BEGIN
            INSERT INTO [dbo].[AuditLogDetails] (
                [Id], [AuditLogId], [FieldName], [OldValue], [NewValue], [DataType], [IsSensitive]
            )
            SELECT 
                NEWID(),
                @AuditLogId,
                JSON_VALUE(value, '$.FieldName'),
                JSON_VALUE(value, '$.OldValue'),
                JSON_VALUE(value, '$.NewValue'),
                JSON_VALUE(value, '$.DataType'),
                CAST(JSON_VALUE(value, '$.IsSensitive') AS BIT)
            FROM OPENJSON(@ChangedFields);
        END
        
        COMMIT TRANSACTION;
        
        -- Return the new audit log ID
        SELECT @AuditLogId AS AuditLogId, @ChangedFieldsCount AS ChangedFieldsCount;
        
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        
        THROW;
    END CATCH
END
GO

PRINT '✅ SP [sp_AuditLogs_Create] created successfully';
GO

-- ================================================
-- SP 2: sp_AuditLogs_GetByEntity
-- Purpose: Retrieve all audit logs for specific entity (paginated)
-- ================================================
IF OBJECT_ID('dbo.sp_AuditLogs_GetByEntity', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_AuditLogs_GetByEntity;
GO

CREATE PROCEDURE dbo.sp_AuditLogs_GetByEntity
    @EntityType NVARCHAR(100),
    @EntityId NVARCHAR(450),
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;
    
    -- Get total count
    DECLARE @TotalCount INT;
    SELECT @TotalCount = COUNT(*)
    FROM [dbo].[AuditLogs]
    WHERE [EntityType] = @EntityType AND [EntityId] = @EntityId;
    
    -- Get paginated audit logs with details
    SELECT 
        al.[Id],
        al.[EntityType],
        al.[EntityId],
        al.[TableName],
        al.[OperationType],
        al.[UserId],
        al.[UserEmail],
        al.[UserFullName],
        al.[SessionId],
        al.[IPAddress],
        al.[UserAgent],
        al.[Timestamp],
        al.[ChangedFieldsCount],
        al.[Notes],
        @TotalCount AS TotalCount,
        @PageNumber AS CurrentPage,
        CEILING(CAST(@TotalCount AS FLOAT) / @PageSize) AS TotalPages
    FROM [dbo].[AuditLogs] al
    WHERE al.[EntityType] = @EntityType AND al.[EntityId] = @EntityId
    ORDER BY al.[Timestamp] DESC
    OFFSET @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY;
    
    -- Get details for returned audit logs
    SELECT 
        ald.[Id],
        ald.[AuditLogId],
        ald.[FieldName],
        ald.[OldValue],
        ald.[NewValue],
        ald.[DataType],
        ald.[IsSensitive]
    FROM [dbo].[AuditLogDetails] ald
    WHERE ald.[AuditLogId] IN (
        SELECT al.[Id]
        FROM [dbo].[AuditLogs] al
        WHERE al.[EntityType] = @EntityType AND al.[EntityId] = @EntityId
        ORDER BY al.[Timestamp] DESC
        OFFSET @Offset ROWS
        FETCH NEXT @PageSize ROWS ONLY
    )
    ORDER BY ald.[FieldName];
END
GO

PRINT '✅ SP [sp_AuditLogs_GetByEntity] created successfully';
GO

-- ================================================
-- SP 3: sp_AuditLogs_GetByUser
-- Purpose: Retrieve all audit logs for specific user (paginated)
-- ================================================
IF OBJECT_ID('dbo.sp_AuditLogs_GetByUser', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_AuditLogs_GetByUser;
GO

CREATE PROCEDURE dbo.sp_AuditLogs_GetByUser
    @UserId UNIQUEIDENTIFIER,
    @StartDate DATETIME2 = NULL,
    @EndDate DATETIME2 = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Default date range: last 30 days
    IF @StartDate IS NULL
        SET @StartDate = DATEADD(DAY, -30, GETDATE());
    IF @EndDate IS NULL
        SET @EndDate = GETDATE();
    
    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;
    
    -- Get total count
    DECLARE @TotalCount INT;
    SELECT @TotalCount = COUNT(*)
    FROM [dbo].[AuditLogs]
    WHERE [UserId] = @UserId 
        AND [Timestamp] BETWEEN @StartDate AND @EndDate;
    
    -- Get paginated audit logs
    SELECT 
        al.[Id],
        al.[EntityType],
        al.[EntityId],
        al.[TableName],
        al.[OperationType],
        al.[UserId],
        al.[UserEmail],
        al.[UserFullName],
        al.[SessionId],
        al.[IPAddress],
        al.[UserAgent],
        al.[Timestamp],
        al.[ChangedFieldsCount],
        al.[Notes],
        @TotalCount AS TotalCount,
        @PageNumber AS CurrentPage,
        CEILING(CAST(@TotalCount AS FLOAT) / @PageSize) AS TotalPages
    FROM [dbo].[AuditLogs] al
    WHERE al.[UserId] = @UserId 
        AND al.[Timestamp] BETWEEN @StartDate AND @EndDate
    ORDER BY al.[Timestamp] DESC
    OFFSET @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END
GO

PRINT '✅ SP [sp_AuditLogs_GetByUser] created successfully';
GO

-- ================================================
-- SP 4: sp_AuditLogs_GetBySession
-- Purpose: Retrieve all audit logs for specific session
-- ================================================
IF OBJECT_ID('dbo.sp_AuditLogs_GetBySession', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_AuditLogs_GetBySession;
GO

CREATE PROCEDURE dbo.sp_AuditLogs_GetBySession
    @SessionId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Get all audit logs for session (no pagination - typically small result set)
    SELECT 
        al.[Id],
        al.[EntityType],
        al.[EntityId],
        al.[TableName],
        al.[OperationType],
        al.[UserId],
        al.[UserEmail],
        al.[UserFullName],
        al.[SessionId],
        al.[IPAddress],
        al.[UserAgent],
        al.[Timestamp],
        al.[ChangedFieldsCount],
        al.[Notes]
    FROM [dbo].[AuditLogs] al
    WHERE al.[SessionId] = @SessionId
    ORDER BY al.[Timestamp] ASC;  -- Chronological order for session timeline
    
    -- Get details for all session audit logs
    SELECT 
        ald.[Id],
        ald.[AuditLogId],
        ald.[FieldName],
        ald.[OldValue],
        ald.[NewValue],
        ald.[DataType],
        ald.[IsSensitive]
    FROM [dbo].[AuditLogDetails] ald
    WHERE ald.[AuditLogId] IN (
        SELECT al.[Id]
        FROM [dbo].[AuditLogs] al
        WHERE al.[SessionId] = @SessionId
    )
    ORDER BY ald.[AuditLogId], ald.[FieldName];
END
GO

PRINT '✅ SP [sp_AuditLogs_GetBySession] created successfully';
GO

-- ================================================
-- SP 5: sp_AuditLogs_GetById
-- Purpose: Retrieve single audit log with all details
-- ================================================
IF OBJECT_ID('dbo.sp_AuditLogs_GetById', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_AuditLogs_GetById;
GO

CREATE PROCEDURE dbo.sp_AuditLogs_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Get audit log
    SELECT 
        al.[Id],
        al.[EntityType],
        al.[EntityId],
        al.[TableName],
        al.[OperationType],
        al.[UserId],
        al.[UserEmail],
        al.[UserFullName],
        al.[SessionId],
        al.[IPAddress],
        al.[UserAgent],
        al.[Timestamp],
        al.[ChangedFieldsCount],
        al.[Notes],
        al.[CreatedAt]
    FROM [dbo].[AuditLogs] al
    WHERE al.[Id] = @Id;
    
    -- Get details
    SELECT 
        ald.[Id],
        ald.[AuditLogId],
        ald.[FieldName],
        ald.[OldValue],
        ald.[NewValue],
        ald.[DataType],
        ald.[IsSensitive]
    FROM [dbo].[AuditLogDetails] ald
    WHERE ald.[AuditLogId] = @Id
    ORDER BY ald.[FieldName];
END
GO

PRINT '✅ SP [sp_AuditLogs_GetById] created successfully';
GO

-- ================================================
-- SP 6: sp_AuditLogs_GetStatistics
-- Purpose: Calculate audit statistics for dashboard
-- ================================================
IF OBJECT_ID('dbo.sp_AuditLogs_GetStatistics', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_AuditLogs_GetStatistics;
GO

CREATE PROCEDURE dbo.sp_AuditLogs_GetStatistics
    @StartDate DATETIME2 = NULL,
    @EndDate DATETIME2 = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Default date range: last 30 days
    IF @StartDate IS NULL
        SET @StartDate = DATEADD(DAY, -30, GETDATE());
    IF @EndDate IS NULL
        SET @EndDate = GETDATE();
    
    -- Total changes by operation type
    SELECT 
        [OperationType],
        COUNT(*) AS [Count]
    FROM [dbo].[AuditLogs]
    WHERE [Timestamp] BETWEEN @StartDate AND @EndDate
    GROUP BY [OperationType]
    ORDER BY [Count] DESC;
    
    -- Total changes by entity type
    SELECT 
        [EntityType],
        COUNT(*) AS [Count]
    FROM [dbo].[AuditLogs]
    WHERE [Timestamp] BETWEEN @StartDate AND @EndDate
    GROUP BY [EntityType]
    ORDER BY [Count] DESC;
    
    -- Top 10 most active users
    SELECT TOP 10
        [UserId],
        [UserEmail],
        [UserFullName],
        COUNT(*) AS [ChangeCount]
    FROM [dbo].[AuditLogs]
    WHERE [Timestamp] BETWEEN @StartDate AND @EndDate
    GROUP BY [UserId], [UserEmail], [UserFullName]
    ORDER BY [ChangeCount] DESC;
    
    -- Changes over time (daily aggregation)
    SELECT 
        CAST([Timestamp] AS DATE) AS [Date],
        COUNT(*) AS [Count]
    FROM [dbo].[AuditLogs]
    WHERE [Timestamp] BETWEEN @StartDate AND @EndDate
    GROUP BY CAST([Timestamp] AS DATE)
    ORDER BY [Date] ASC;
    
    -- Summary stats
    SELECT 
        COUNT(*) AS TotalChanges,
        COUNT(DISTINCT [UserId]) AS UniqueUsers,
        COUNT(DISTINCT [SessionId]) AS UniqueSessions,
        COUNT(DISTINCT [EntityType]) AS EntityTypesModified,
        MIN([Timestamp]) AS FirstChange,
        MAX([Timestamp]) AS LastChange
    FROM [dbo].[AuditLogs]
    WHERE [Timestamp] BETWEEN @StartDate AND @EndDate;
END
GO

PRINT '✅ SP [sp_AuditLogs_GetStatistics] created successfully';
GO

-- ================================================
-- SP 7: sp_AuditLogs_Search
-- Purpose: Advanced search with multiple filters
-- ================================================
IF OBJECT_ID('dbo.sp_AuditLogs_Search', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_AuditLogs_Search;
GO

CREATE PROCEDURE dbo.sp_AuditLogs_Search
    @EntityType NVARCHAR(100) = NULL,
    @UserId UNIQUEIDENTIFIER = NULL,
    @OperationType NVARCHAR(20) = NULL,
    @StartDate DATETIME2 = NULL,
    @EndDate DATETIME2 = NULL,
    @SearchTerm NVARCHAR(200) = NULL,  -- Search in EntityId, UserEmail, UserFullName, Notes
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Default date range: last 30 days
    IF @StartDate IS NULL
        SET @StartDate = DATEADD(DAY, -30, GETDATE());
    IF @EndDate IS NULL
        SET @EndDate = GETDATE();
    
    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;
    
    -- Build dynamic WHERE clause
    DECLARE @TotalCount INT;
    
    SELECT @TotalCount = COUNT(*)
    FROM [dbo].[AuditLogs]
    WHERE (@EntityType IS NULL OR [EntityType] = @EntityType)
        AND (@UserId IS NULL OR [UserId] = @UserId)
        AND (@OperationType IS NULL OR [OperationType] = @OperationType)
        AND [Timestamp] BETWEEN @StartDate AND @EndDate
        AND (@SearchTerm IS NULL OR 
             [EntityId] LIKE '%' + @SearchTerm + '%' OR
             [UserEmail] LIKE '%' + @SearchTerm + '%' OR
             [UserFullName] LIKE '%' + @SearchTerm + '%' OR
             [Notes] LIKE '%' + @SearchTerm + '%');
    
    -- Get paginated results
    SELECT 
        al.[Id],
        al.[EntityType],
        al.[EntityId],
        al.[TableName],
        al.[OperationType],
        al.[UserId],
        al.[UserEmail],
        al.[UserFullName],
        al.[SessionId],
        al.[IPAddress],
        al.[UserAgent],
        al.[Timestamp],
        al.[ChangedFieldsCount],
        al.[Notes],
        @TotalCount AS TotalCount,
        @PageNumber AS CurrentPage,
        CEILING(CAST(@TotalCount AS FLOAT) / @PageSize) AS TotalPages
    FROM [dbo].[AuditLogs] al
    WHERE (@EntityType IS NULL OR al.[EntityType] = @EntityType)
        AND (@UserId IS NULL OR al.[UserId] = @UserId)
        AND (@OperationType IS NULL OR al.[OperationType] = @OperationType)
        AND al.[Timestamp] BETWEEN @StartDate AND @EndDate
        AND (@SearchTerm IS NULL OR 
             al.[EntityId] LIKE '%' + @SearchTerm + '%' OR
             al.[UserEmail] LIKE '%' + @SearchTerm + '%' OR
             al.[UserFullName] LIKE '%' + @SearchTerm + '%' OR
             al.[Notes] LIKE '%' + @SearchTerm + '%')
    ORDER BY al.[Timestamp] DESC
    OFFSET @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END
GO

PRINT '✅ SP [sp_AuditLogs_Search] created successfully';
GO

-- ================================================
-- SP 8: sp_AuditLogs_DeleteOld
-- Purpose: Cleanup old audit logs based on retention policy
-- ================================================
IF OBJECT_ID('dbo.sp_AuditLogs_DeleteOld', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_AuditLogs_DeleteOld;
GO

CREATE PROCEDURE dbo.sp_AuditLogs_DeleteOld
    @RetentionDays INT = 730  -- Default: 2 years
AS
BEGIN
    SET NOCOUNT ON;
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        DECLARE @CutoffDate DATETIME2 = DATEADD(DAY, -@RetentionDays, GETDATE());
        DECLARE @DeletedCount INT;
        
        -- Delete old audit logs (CASCADE DELETE will remove details automatically)
        DELETE FROM [dbo].[AuditLogs]
        WHERE [Timestamp] < @CutoffDate;
        
        SET @DeletedCount = @@ROWCOUNT;
        
        COMMIT TRANSACTION;
        
        -- Return deleted count
        SELECT 
            @DeletedCount AS DeletedAuditLogs,
            @CutoffDate AS CutoffDate,
            @RetentionDays AS RetentionDays,
            GETDATE() AS ExecutedAt;
        
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        
        THROW;
    END CATCH
END
GO

PRINT '✅ SP [sp_AuditLogs_DeleteOld] created successfully';
GO

-- ================================================
-- SP 9: sp_AuditLogs_GetUserOwnActivity
-- Purpose: User views their own audit trail (privacy compliance)
-- ================================================
IF OBJECT_ID('dbo.sp_AuditLogs_GetUserOwnActivity', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_AuditLogs_GetUserOwnActivity;
GO

CREATE PROCEDURE dbo.sp_AuditLogs_GetUserOwnActivity
    @UserId UNIQUEIDENTIFIER,
    @StartDate DATETIME2 = NULL,
    @EndDate DATETIME2 = NULL,
    @PageNumber INT = 1,
    @PageSize INT = 50
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Default date range: last 30 days
    IF @StartDate IS NULL
        SET @StartDate = DATEADD(DAY, -30, GETDATE());
    IF @EndDate IS NULL
        SET @EndDate = GETDATE();
    
    DECLARE @Offset INT = (@PageNumber - 1) * @PageSize;
    
    -- Get total count
    DECLARE @TotalCount INT;
    SELECT @TotalCount = COUNT(*)
    FROM [dbo].[AuditLogs]
    WHERE [UserId] = @UserId 
        AND [Timestamp] BETWEEN @StartDate AND @EndDate;
    
    -- Get paginated audit logs (without sensitive IP/UserAgent for privacy)
    SELECT 
        al.[Id],
        al.[EntityType],
        al.[EntityId],
        al.[TableName],
        al.[OperationType],
        al.[Timestamp],
        al.[ChangedFieldsCount],
        al.[Notes],
        -- Exclude: IPAddress, UserAgent, SessionId for privacy
        @TotalCount AS TotalCount,
        @PageNumber AS CurrentPage,
        CEILING(CAST(@TotalCount AS FLOAT) / @PageSize) AS TotalPages
    FROM [dbo].[AuditLogs] al
    WHERE al.[UserId] = @UserId 
        AND al.[Timestamp] BETWEEN @StartDate AND @EndDate
    ORDER BY al.[Timestamp] DESC
    OFFSET @Offset ROWS
    FETCH NEXT @PageSize ROWS ONLY;
    
    -- Get details for returned audit logs
    SELECT 
        ald.[Id],
        ald.[AuditLogId],
        ald.[FieldName],
        ald.[OldValue],
        ald.[NewValue],
        ald.[DataType],
        ald.[IsSensitive]
    FROM [dbo].[AuditLogDetails] ald
    WHERE ald.[AuditLogId] IN (
        SELECT al.[Id]
        FROM [dbo].[AuditLogs] al
        WHERE al.[UserId] = @UserId 
            AND al.[Timestamp] BETWEEN @StartDate AND @EndDate
        ORDER BY al.[Timestamp] DESC
        OFFSET @Offset ROWS
        FETCH NEXT @PageSize ROWS ONLY
    )
    ORDER BY ald.[FieldName];
END
GO

PRINT '✅ SP [sp_AuditLogs_GetUserOwnActivity] created successfully';
GO

-- ================================================
-- VERIFICATION
-- ================================================
PRINT '';
PRINT '================================================';
PRINT '✅ AUDIT STORED PROCEDURES CREATED SUCCESSFULLY';
PRINT '================================================';
PRINT '';

-- List all audit stored procedures
SELECT 
    ROUTINE_NAME AS StoredProcedure,
    CREATED AS CreatedDate,
    LAST_ALTERED AS LastModified
FROM INFORMATION_SCHEMA.ROUTINES
WHERE ROUTINE_TYPE = 'PROCEDURE'
    AND ROUTINE_NAME LIKE 'sp_AuditLogs_%'
ORDER BY ROUTINE_NAME;

PRINT '';
PRINT '📊 Stored Procedures: 9';
PRINT '  1. sp_AuditLogs_Create - Insert audit log with details';
PRINT '  2. sp_AuditLogs_GetByEntity - Get logs for entity (paginated)';
PRINT '  3. sp_AuditLogs_GetByUser - Get logs for user (paginated)';
PRINT '  4. sp_AuditLogs_GetBySession - Get logs for session';
PRINT '  5. sp_AuditLogs_GetById - Get single log with details';
PRINT '  6. sp_AuditLogs_GetStatistics - Dashboard statistics';
PRINT '  7. sp_AuditLogs_Search - Advanced search (multi-filter)';
PRINT '  8. sp_AuditLogs_DeleteOld - Cleanup old logs';
PRINT '  9. sp_AuditLogs_GetUserOwnActivity - User own activity (privacy)';
PRINT '';
PRINT '🚀 Next Step: FAZA 3 - Backend Models & Services';
PRINT '';
GO
