-- ================================================
-- Script: 013_AuditSystem.sql
-- Description: Create AuditLogs and AuditLogDetails tables for comprehensive audit trail
-- Author: Copilot + User
-- Date: 2026-01-09
-- Dependencies: 001_CreateDatabase.sql, 006_CreateSessions.sql
-- ================================================

USE [ValyanERP];
GO

-- ================================================
-- DROP EXISTING TABLES (if any - for clean re-run)
-- ================================================
IF OBJECT_ID('dbo.AuditLogDetails', 'U') IS NOT NULL
    DROP TABLE [dbo].[AuditLogDetails];
GO

IF OBJECT_ID('dbo.AuditLogs', 'U') IS NOT NULL
    DROP TABLE [dbo].[AuditLogs];
GO

PRINT '✅ Old audit tables dropped (if existed)';
GO

-- ================================================
-- TABLE 1: AuditLogs (Master Audit Log Table)
-- ================================================
CREATE TABLE [dbo].[AuditLogs] (
    -- Primary Key
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    
    -- Entity Tracking (WHAT was changed)
    [EntityType] NVARCHAR(100) NOT NULL,        -- e.g., "Persoane", "Users", "SystemParameters", "Sessions"
    [EntityId] NVARCHAR(450) NOT NULL,          -- GUID or composite key (flexible string)
    [TableName] NVARCHAR(100) NOT NULL,         -- Actual SQL table name (e.g., "Persoane", "AspNetUsers")
    [OperationType] NVARCHAR(20) NOT NULL,      -- "Create", "Update", "Delete"
    
    -- User Context (WHO made the change)
    [UserId] UNIQUEIDENTIFIER NOT NULL,         -- FK to Users.Id
    [UserEmail] NVARCHAR(256) NOT NULL,         -- Denormalized for performance (avoid JOIN)
    [UserFullName] NVARCHAR(200) NULL,          -- Denormalized for display (FirstName + LastName)
    
    -- Session Context (WHERE/HOW)
    [SessionId] UNIQUEIDENTIFIER NULL,          -- FK to Sessions.Id (NULL if system operation)
    [IPAddress] NVARCHAR(45) NULL,              -- IPv4 (15 chars) or IPv6 (45 chars)
    [UserAgent] NVARCHAR(500) NULL,             -- Browser/Client info (truncated if too long)
    
    -- Timestamp (WHEN)
    [Timestamp] DATETIME2 NOT NULL DEFAULT GETDATE(),
    
    -- Metadata
    [ChangedFieldsCount] INT NOT NULL DEFAULT 0, -- Number of fields changed (for Update operations)
    [Notes] NVARCHAR(MAX) NULL,                 -- Optional manual notes (e.g., "Bulk import", "Admin override")
    
    -- Audit Fields (for the audit table itself)
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    
    -- Constraints
    CONSTRAINT [CHK_AuditLogs_OperationType] CHECK ([OperationType] IN ('Create', 'Update', 'Delete'))
);
GO

PRINT '✅ Table [dbo].[AuditLogs] created successfully';
GO

-- ================================================
-- TABLE 2: AuditLogDetails (Field-Level Change Tracking)
-- ================================================
CREATE TABLE [dbo].[AuditLogDetails] (
    -- Primary Key
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    
    -- Link to Master Audit Log
    [AuditLogId] UNIQUEIDENTIFIER NOT NULL,
    
    -- Field-Level Change Tracking
    [FieldName] NVARCHAR(100) NOT NULL,         -- Property name (e.g., "FirstName", "Email", "CNP")
    [OldValue] NVARCHAR(MAX) NULL,              -- Previous value (NULL for Create, masked if sensitive)
    [NewValue] NVARCHAR(MAX) NULL,              -- New value (NULL for Delete, masked if sensitive)
    [DataType] NVARCHAR(50) NULL,               -- "string", "int", "datetime", "bool", "decimal", "guid"
    [IsSensitive] BIT NOT NULL DEFAULT 0        -- TRUE if field was masked (Password, CNP, etc.)
);
GO

PRINT '✅ Table [dbo].[AuditLogDetails] created successfully';
GO

-- ================================================
-- FOREIGN KEYS (Added after tables created)
-- ================================================

-- FK: AuditLogs -> Users
ALTER TABLE [dbo].[AuditLogs]
    ADD CONSTRAINT [FK_AuditLogs_UserId] FOREIGN KEY ([UserId]) 
        REFERENCES [dbo].[Users]([Id]) ON DELETE NO ACTION;
GO
PRINT '✅ Foreign Key [FK_AuditLogs_UserId] created';

-- FK: AuditLogs -> Sessions
ALTER TABLE [dbo].[AuditLogs]
    ADD CONSTRAINT [FK_AuditLogs_SessionId] FOREIGN KEY ([SessionId]) 
        REFERENCES [dbo].[Sessions]([Id]) ON DELETE SET NULL;
GO
PRINT '✅ Foreign Key [FK_AuditLogs_SessionId] created';

-- FK: AuditLogDetails -> AuditLogs (CASCADE DELETE for cleanup)
ALTER TABLE [dbo].[AuditLogDetails]
    ADD CONSTRAINT [FK_AuditLogDetails_AuditLogId] FOREIGN KEY ([AuditLogId]) 
        REFERENCES [dbo].[AuditLogs]([Id]) ON DELETE CASCADE;
GO
PRINT '✅ Foreign Key [FK_AuditLogDetails_AuditLogId] created';
GO

-- ================================================
-- INDEXES for Performance
-- ================================================

-- INDEX 1: Search by Entity (most common query)
CREATE NONCLUSTERED INDEX [IX_AuditLogs_EntityType_EntityId] 
    ON [dbo].[AuditLogs] ([EntityType] ASC, [EntityId] ASC)
    INCLUDE ([OperationType], [Timestamp], [UserEmail]);
GO
PRINT '✅ Index [IX_AuditLogs_EntityType_EntityId] created';

-- INDEX 2: Search by User Activity
CREATE NONCLUSTERED INDEX [IX_AuditLogs_UserId_Timestamp] 
    ON [dbo].[AuditLogs] ([UserId] ASC, [Timestamp] DESC)
    INCLUDE ([EntityType], [OperationType]);
GO
PRINT '✅ Index [IX_AuditLogs_UserId_Timestamp] created';

-- INDEX 3: Search by Timestamp (for time-based queries and cleanup)
CREATE NONCLUSTERED INDEX [IX_AuditLogs_Timestamp] 
    ON [dbo].[AuditLogs] ([Timestamp] DESC)
    INCLUDE ([EntityType], [OperationType]);
GO
PRINT '✅ Index [IX_AuditLogs_Timestamp] created';

-- INDEX 4: Filter by Operation Type
CREATE NONCLUSTERED INDEX [IX_AuditLogs_OperationType] 
    ON [dbo].[AuditLogs] ([OperationType] ASC)
    INCLUDE ([EntityType], [Timestamp]);
GO
PRINT '✅ Index [IX_AuditLogs_OperationType] created';

-- INDEX 5: Session Activity (all changes in a session)
CREATE NONCLUSTERED INDEX [IX_AuditLogs_SessionId] 
    ON [dbo].[AuditLogs] ([SessionId] ASC)
    WHERE [SessionId] IS NOT NULL
    INCLUDE ([Timestamp], [EntityType], [OperationType]);
GO
PRINT '✅ Index [IX_AuditLogs_SessionId] created (filtered)';

-- INDEX 6: Table-specific queries
CREATE NONCLUSTERED INDEX [IX_AuditLogs_TableName] 
    ON [dbo].[AuditLogs] ([TableName] ASC, [Timestamp] DESC);
GO
PRINT '✅ Index [IX_AuditLogs_TableName] created';

-- INDEX 7: Details by AuditLogId (for JOIN performance)
CREATE NONCLUSTERED INDEX [IX_AuditLogDetails_AuditLogId] 
    ON [dbo].[AuditLogDetails] ([AuditLogId] ASC)
    INCLUDE ([FieldName], [OldValue], [NewValue]);
GO
PRINT '✅ Index [IX_AuditLogDetails_AuditLogId] created';

-- INDEX 8: Search by Field Name (e.g., "show all Email changes")
CREATE NONCLUSTERED INDEX [IX_AuditLogDetails_FieldName] 
    ON [dbo].[AuditLogDetails] ([FieldName] ASC)
    INCLUDE ([AuditLogId]);
GO
PRINT '✅ Index [IX_AuditLogDetails_FieldName] created';

-- ================================================
-- SEED: Add Audit Configuration to SystemParameters
-- ================================================

-- Check if SystemParameters table exists
IF OBJECT_ID('dbo.SystemParameters', 'U') IS NOT NULL
BEGIN
    -- Audit Retention Period
    IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Audit.Retention.Days')
    BEGIN
        INSERT INTO [dbo].[SystemParameters] (
            [ParameterKey], [Category], [SubCategory], 
            [ParameterValue], [DataType], [DefaultValue],
            [Description], [DisplayName], [IsReadOnly],
            [MinValue], [MaxValue]
        ) VALUES (
            'Audit.Retention.Days',
            'Audit',
            'Retention',
            '730',
            'int',
            '730',
            'Număr de zile de păstrare a audit logs. După această perioadă, log-urile vechi sunt șterse automat. Valori recomandate: 365 (1 an), 730 (2 ani), 1825 (5 ani). Interval valid: 30-3650 zile.',
            'Retenție Audit Logs (zile)',
            0,
            30,
            3650
        );
        PRINT '✅ Parameter [Audit.Retention.Days] added = 730 (2 years)';
    END

    -- Audit Cleanup Enabled
    IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Audit.Cleanup.Enabled')
    BEGIN
        INSERT INTO [dbo].[SystemParameters] (
            [ParameterKey], [Category], [SubCategory], 
            [ParameterValue], [DataType], [DefaultValue],
            [Description], [DisplayName], [IsReadOnly]
        ) VALUES (
            'Audit.Cleanup.Enabled',
            'Audit',
            'Cleanup',
            'true',
            'bool',
            'true',
            'Activează ștergerea automată a audit logs vechi conform politicii de retenție. Dacă este dezactivat, log-urile nu vor fi niciodată șterse automat (necesită ștergere manuală).',
            'Activare Cleanup Automat',
            0
        );
        PRINT '✅ Parameter [Audit.Cleanup.Enabled] added = true';
    END

    -- Audit Cleanup Schedule (Cron Expression)
    IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Audit.Cleanup.ScheduleCron')
    BEGIN
        INSERT INTO [dbo].[SystemParameters] (
            [ParameterKey], [Category], [SubCategory], 
            [ParameterValue], [DataType], [DefaultValue],
            [Description], [DisplayName], [IsReadOnly],
            [ValidationRegex]
        ) VALUES (
            'Audit.Cleanup.ScheduleCron',
            'Audit',
            'Cleanup',
            '0 2 * * 0',
            'string',
            '0 2 * * 0',
            'Schedule Cron pentru job-ul de cleanup audit logs. Default: "0 2 * * 0" = Duminică la 02:00 AM. Format: minute hour day month weekday. Exemplu: "0 3 * * *" = zilnic la 03:00.',
            'Programare Cleanup (Cron)',
            0,
            '^[0-9\*\-\,\/\s]+$'
        );
        PRINT '✅ Parameter [Audit.Cleanup.ScheduleCron] added = "0 2 * * 0" (Sunday 2 AM)';
    END

    -- Audit Track IP Addresses
    IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Audit.TrackIPAddress')
    BEGIN
        INSERT INTO [dbo].[SystemParameters] (
            [ParameterKey], [Category], [SubCategory], 
            [ParameterValue], [DataType], [DefaultValue],
            [Description], [DisplayName], [IsReadOnly]
        ) VALUES (
            'Audit.TrackIPAddress',
            'Audit',
            'Tracking',
            'true',
            'bool',
            'true',
            'Activează tracking-ul adreselor IP în audit logs. Util pentru securitate și analiză, dar poate avea implicații GDPR. Dacă este dezactivat, IPAddress va fi NULL.',
            'Track IP Addresses',
            0
        );
        PRINT '✅ Parameter [Audit.TrackIPAddress] added = true';
    END

    -- Audit Track User Agent
    IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Audit.TrackUserAgent')
    BEGIN
        INSERT INTO [dbo].[SystemParameters] (
            [ParameterKey], [Category], [SubCategory], 
            [ParameterValue], [DataType], [DefaultValue],
            [Description], [DisplayName], [IsReadOnly]
        ) VALUES (
            'Audit.TrackUserAgent',
            'Audit',
            'Tracking',
            'true',
            'bool',
            'true',
            'Activează tracking-ul browser/client info (User Agent) în audit logs. Util pentru identificare dispozitiv și debugging. Dacă este dezactivat, UserAgent va fi NULL.',
            'Track User Agent',
            0
        );
        PRINT '✅ Parameter [Audit.TrackUserAgent] added = true';
    END
END
ELSE
BEGIN
    PRINT '⚠️ SystemParameters table not found - skipping parameter seeding';
END
GO

-- ================================================
-- VERIFICATION
-- ================================================
PRINT '';
PRINT '================================================';
PRINT '✅ AUDIT SYSTEM SCHEMA CREATED SUCCESSFULLY';
PRINT '================================================';
PRINT '';

-- Count tables
SELECT 
    'AuditLogs' AS [Table],
    COUNT(*) AS [RowCount],
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AuditLogs') AND index_id > 0) AS [IndexCount]
FROM [dbo].[AuditLogs]
UNION ALL
SELECT 
    'AuditLogDetails' AS [Table],
    COUNT(*) AS [RowCount],
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AuditLogDetails') AND index_id > 0) AS [IndexCount]
FROM [dbo].[AuditLogDetails];

PRINT '';
PRINT '📊 Tables: 2 (AuditLogs, AuditLogDetails)';
PRINT '📈 Indexes: 8 (6 on AuditLogs, 2 on AuditLogDetails)';
PRINT '⚙️ SystemParameters: 5 new audit configuration parameters';
PRINT '';
PRINT '🚀 Next Step: Run 014_StoredProcedures_Audit.sql';
PRINT '';
GO
