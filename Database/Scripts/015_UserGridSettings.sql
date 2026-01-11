-- ================================================================
-- UserGridSettings Table and Stored Procedures
-- Purpose: Store user-specific Syncfusion DataGrid configurations
-- Created: 2026-01-10
-- ================================================================

USE [ValyanERP]
GO

-- ================================================================
-- Table: UserGridSettings
-- ================================================================
IF OBJECT_ID('dbo.UserGridSettings', 'U') IS NOT NULL
    DROP TABLE dbo.UserGridSettings;
GO

CREATE TABLE [dbo].[UserGridSettings] (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    
    -- User reference
    [UserId] UNIQUEIDENTIFIER NOT NULL,
    
    -- Grid identification
    [GridId] NVARCHAR(100) NOT NULL,           -- e.g., 'Utilizatori', 'Persoane', 'Facturi'
    [ConfigurationName] NVARCHAR(200) NOT NULL, -- e.g., 'Default', 'My Custom View', 'Report View'
    
    -- Grid state (JSON serialized from Syncfusion GetPersistDataAsync)
    [GridState] NVARCHAR(MAX) NOT NULL,        -- JSON: columns, sorting, filtering, grouping, etc.
    
    -- Configuration flags
    [IsDefault] BIT NOT NULL DEFAULT 0,         -- Is this the default configuration for this grid?
    [IsActive] BIT NOT NULL DEFAULT 1,          -- Soft delete flag
    
    -- Audit columns
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIME2 NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    
    -- Constraints
    CONSTRAINT [FK_UserGridSettings_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users]([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_UserGridSettings_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_UserGridSettings_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id])
);
GO

-- Indexes
CREATE INDEX [IX_UserGridSettings_UserId] ON [dbo].[UserGridSettings] ([UserId]);
CREATE INDEX [IX_UserGridSettings_GridId] ON [dbo].[UserGridSettings] ([GridId]);
CREATE INDEX [IX_UserGridSettings_UserId_GridId] ON [dbo].[UserGridSettings] ([UserId], [GridId]);
CREATE INDEX [IX_UserGridSettings_UserId_GridId_IsDefault] ON [dbo].[UserGridSettings] ([UserId], [GridId], [IsDefault]) WHERE [IsActive] = 1;
GO

-- Unique constraint: Only one default configuration per user per grid
CREATE UNIQUE INDEX [UX_UserGridSettings_UserId_GridId_IsDefault] 
ON [dbo].[UserGridSettings] ([UserId], [GridId]) 
WHERE [IsDefault] = 1 AND [IsActive] = 1;
GO

-- Unique constraint: Configuration name must be unique per user per grid
CREATE UNIQUE INDEX [UX_UserGridSettings_UserId_GridId_Name] 
ON [dbo].[UserGridSettings] ([UserId], [GridId], [ConfigurationName]) 
WHERE [IsActive] = 1;
GO

PRINT 'Table UserGridSettings created successfully.';
GO

-- ================================================================
-- Stored Procedure: sp_UserGridSettings_GetAll
-- ================================================================
IF OBJECT_ID('dbo.sp_UserGridSettings_GetAll', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserGridSettings_GetAll;
GO

CREATE PROCEDURE dbo.sp_UserGridSettings_GetAll
    @UserId UNIQUEIDENTIFIER,
    @GridId NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        [Id],
        [UserId],
        [GridId],
        [ConfigurationName],
        [GridState],
        [IsDefault],
        [IsActive],
        [CreatedAt],
        [CreatedBy],
        [UpdatedAt],
        [UpdatedBy]
    FROM [dbo].[UserGridSettings]
    WHERE [UserId] = @UserId 
      AND [GridId] = @GridId
      AND [IsActive] = 1
    ORDER BY [IsDefault] DESC, [ConfigurationName] ASC;
END
GO

PRINT 'Stored procedure sp_UserGridSettings_GetAll created.';
GO

-- ================================================================
-- Stored Procedure: sp_UserGridSettings_GetById
-- ================================================================
IF OBJECT_ID('dbo.sp_UserGridSettings_GetById', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserGridSettings_GetById;
GO

CREATE PROCEDURE dbo.sp_UserGridSettings_GetById
    @Id UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT 
        [Id],
        [UserId],
        [GridId],
        [ConfigurationName],
        [GridState],
        [IsDefault],
        [IsActive],
        [CreatedAt],
        [CreatedBy],
        [UpdatedAt],
        [UpdatedBy]
    FROM [dbo].[UserGridSettings]
    WHERE [Id] = @Id AND [IsActive] = 1;
END
GO

PRINT 'Stored procedure sp_UserGridSettings_GetById created.';
GO

-- ================================================================
-- Stored Procedure: sp_UserGridSettings_GetDefault
-- ================================================================
IF OBJECT_ID('dbo.sp_UserGridSettings_GetDefault', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserGridSettings_GetDefault;
GO

CREATE PROCEDURE dbo.sp_UserGridSettings_GetDefault
    @UserId UNIQUEIDENTIFIER,
    @GridId NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    
    SELECT TOP 1
        [Id],
        [UserId],
        [GridId],
        [ConfigurationName],
        [GridState],
        [IsDefault],
        [IsActive],
        [CreatedAt],
        [CreatedBy],
        [UpdatedAt],
        [UpdatedBy]
    FROM [dbo].[UserGridSettings]
    WHERE [UserId] = @UserId 
      AND [GridId] = @GridId
      AND [IsDefault] = 1
      AND [IsActive] = 1;
END
GO

PRINT 'Stored procedure sp_UserGridSettings_GetDefault created.';
GO

-- ================================================================
-- Stored Procedure: sp_UserGridSettings_Create
-- ================================================================
IF OBJECT_ID('dbo.sp_UserGridSettings_Create', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserGridSettings_Create;
GO

CREATE PROCEDURE dbo.sp_UserGridSettings_Create
    @Id UNIQUEIDENTIFIER OUTPUT,
    @UserId UNIQUEIDENTIFIER,
    @GridId NVARCHAR(100),
    @ConfigurationName NVARCHAR(200),
    @GridState NVARCHAR(MAX),
    @IsDefault BIT = 0,
    @CreatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Generate new ID if not provided
    IF @Id IS NULL OR @Id = '00000000-0000-0000-0000-000000000000'
        SET @Id = NEWID();
    
    -- If this is set as default, clear other defaults for this user/grid
    IF @IsDefault = 1
    BEGIN
        UPDATE [dbo].[UserGridSettings]
        SET [IsDefault] = 0,
            [UpdatedAt] = GETDATE(),
            [UpdatedBy] = @CreatedBy
        WHERE [UserId] = @UserId 
          AND [GridId] = @GridId
          AND [IsDefault] = 1
          AND [IsActive] = 1;
    END
    
    INSERT INTO [dbo].[UserGridSettings] (
        [Id],
        [UserId],
        [GridId],
        [ConfigurationName],
        [GridState],
        [IsDefault],
        [IsActive],
        [CreatedAt],
        [CreatedBy]
    )
    VALUES (
        @Id,
        @UserId,
        @GridId,
        @ConfigurationName,
        @GridState,
        @IsDefault,
        1,
        GETDATE(),
        @CreatedBy
    );
    
    SELECT @Id AS Id;
END
GO

PRINT 'Stored procedure sp_UserGridSettings_Create created.';
GO

-- ================================================================
-- Stored Procedure: sp_UserGridSettings_Update
-- ================================================================
IF OBJECT_ID('dbo.sp_UserGridSettings_Update', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserGridSettings_Update;
GO

CREATE PROCEDURE dbo.sp_UserGridSettings_Update
    @Id UNIQUEIDENTIFIER,
    @ConfigurationName NVARCHAR(200),
    @GridState NVARCHAR(MAX),
    @IsDefault BIT,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @UserId UNIQUEIDENTIFIER;
    DECLARE @GridId NVARCHAR(100);
    
    -- Get user and grid info for this setting
    SELECT @UserId = [UserId], @GridId = [GridId]
    FROM [dbo].[UserGridSettings]
    WHERE [Id] = @Id AND [IsActive] = 1;
    
    -- If this is set as default, clear other defaults for this user/grid
    IF @IsDefault = 1
    BEGIN
        UPDATE [dbo].[UserGridSettings]
        SET [IsDefault] = 0,
            [UpdatedAt] = GETDATE(),
            [UpdatedBy] = @UpdatedBy
        WHERE [UserId] = @UserId 
          AND [GridId] = @GridId
          AND [Id] <> @Id
          AND [IsDefault] = 1
          AND [IsActive] = 1;
    END
    
    UPDATE [dbo].[UserGridSettings]
    SET [ConfigurationName] = @ConfigurationName,
        [GridState] = @GridState,
        [IsDefault] = @IsDefault,
        [UpdatedAt] = GETDATE(),
        [UpdatedBy] = @UpdatedBy
    WHERE [Id] = @Id AND [IsActive] = 1;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT 'Stored procedure sp_UserGridSettings_Update created.';
GO

-- ================================================================
-- Stored Procedure: sp_UserGridSettings_Delete (Soft Delete)
-- ================================================================
IF OBJECT_ID('dbo.sp_UserGridSettings_Delete', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserGridSettings_Delete;
GO

CREATE PROCEDURE dbo.sp_UserGridSettings_Delete
    @Id UNIQUEIDENTIFIER,
    @DeletedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE [dbo].[UserGridSettings]
    SET [IsActive] = 0,
        [UpdatedAt] = GETDATE(),
        [UpdatedBy] = @DeletedBy
    WHERE [Id] = @Id AND [IsActive] = 1;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT 'Stored procedure sp_UserGridSettings_Delete created.';
GO

-- ================================================================
-- Stored Procedure: sp_UserGridSettings_SetDefault
-- ================================================================
IF OBJECT_ID('dbo.sp_UserGridSettings_SetDefault', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserGridSettings_SetDefault;
GO

CREATE PROCEDURE dbo.sp_UserGridSettings_SetDefault
    @Id UNIQUEIDENTIFIER,
    @UpdatedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @UserId UNIQUEIDENTIFIER;
    DECLARE @GridId NVARCHAR(100);
    
    -- Get user and grid info for this setting
    SELECT @UserId = [UserId], @GridId = [GridId]
    FROM [dbo].[UserGridSettings]
    WHERE [Id] = @Id AND [IsActive] = 1;
    
    -- Clear all other defaults for this user/grid
    UPDATE [dbo].[UserGridSettings]
    SET [IsDefault] = 0,
        [UpdatedAt] = GETDATE(),
        [UpdatedBy] = @UpdatedBy
    WHERE [UserId] = @UserId 
      AND [GridId] = @GridId
      AND [IsDefault] = 1
      AND [IsActive] = 1;
    
    -- Set this one as default
    UPDATE [dbo].[UserGridSettings]
    SET [IsDefault] = 1,
        [UpdatedAt] = GETDATE(),
        [UpdatedBy] = @UpdatedBy
    WHERE [Id] = @Id AND [IsActive] = 1;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT 'Stored procedure sp_UserGridSettings_SetDefault created.';
GO

-- ================================================================
-- Stored Procedure: sp_UserGridSettings_DeleteAllForGrid
-- Clears all saved configurations for a user on a specific grid
-- ================================================================
IF OBJECT_ID('dbo.sp_UserGridSettings_DeleteAllForGrid', 'P') IS NOT NULL
    DROP PROCEDURE dbo.sp_UserGridSettings_DeleteAllForGrid;
GO

CREATE PROCEDURE dbo.sp_UserGridSettings_DeleteAllForGrid
    @UserId UNIQUEIDENTIFIER,
    @GridId NVARCHAR(100),
    @DeletedBy UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    UPDATE [dbo].[UserGridSettings]
    SET [IsActive] = 0,
        [UpdatedAt] = GETDATE(),
        [UpdatedBy] = @DeletedBy
    WHERE [UserId] = @UserId 
      AND [GridId] = @GridId
      AND [IsActive] = 1;
    
    SELECT @@ROWCOUNT AS RowsAffected;
END
GO

PRINT 'Stored procedure sp_UserGridSettings_DeleteAllForGrid created.';
GO

PRINT '';
PRINT '================================================================';
PRINT 'UserGridSettings table and stored procedures created successfully!';
PRINT '================================================================';
GO
