-- ================================================================
-- SCRIPT: 011_SystemParameters.sql
-- PURPOSE: Create SystemParameters table for centralized configuration management
--          Replaces hardcoded constants and enums with database-driven parameters
-- DATE: 2026-01-09
-- AUTHOR: GitHub Copilot
-- ================================================================

USE ValyanERP;
GO

PRINT '========================================';
PRINT 'Creating SystemParameters Infrastructure';
PRINT '========================================';
GO

-- ================================================================
-- TABLE: SystemParameters
-- PURPOSE: Centralized configuration storage
--          - Application settings
--          - Business rules
--          - Feature flags
--          - Enum values
--          - Cache durations
--          - Validation rules
-- ================================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SystemParameters')
BEGIN
    CREATE TABLE [dbo].[SystemParameters] (
        -- Primary Key
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        
        -- Parameter Identification
        [ParameterKey] NVARCHAR(100) NOT NULL UNIQUE,      -- Unique key (e.g., "Cache.Persoane.Duration")
        [Category] NVARCHAR(50) NOT NULL,                  -- Category (e.g., "Cache", "Validation", "Business")
        [SubCategory] NVARCHAR(50) NULL,                   -- Subcategory (e.g., "Persoane", "Users")
        
        -- Parameter Value (flexible storage)
        [ParameterValue] NVARCHAR(500) NOT NULL,           -- String representation of value
        [DataType] NVARCHAR(20) NOT NULL,                  -- int, string, bool, decimal, json
        [DefaultValue] NVARCHAR(500) NULL,                 -- Default if value is invalid
        
        -- Metadata
        [Description] NVARCHAR(500) NOT NULL,              -- Human-readable description
        [DisplayName] NVARCHAR(100) NOT NULL,              -- UI-friendly name
        [IsReadOnly] BIT NOT NULL DEFAULT 0,               -- Prevent modification (system critical)
        [IsEncrypted] BIT NOT NULL DEFAULT 0,              -- Value is encrypted (passwords, API keys)
        [ValidationRegex] NVARCHAR(200) NULL,              -- Optional regex for validation
        [MinValue] DECIMAL(18,2) NULL,                     -- Min value for numeric types
        [MaxValue] DECIMAL(18,2) NULL,                     -- Max value for numeric types
        
        -- Audit
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT (SYSUTCDATETIME() AT TIME ZONE 'UTC' AT TIME ZONE 'E. Europe Standard Time'),
        [CreatedBy] UNIQUEIDENTIFIER NULL,
        [UpdatedAt] DATETIME2 NULL,
        [UpdatedBy] UNIQUEIDENTIFIER NULL,
        
        -- Foreign Keys
        CONSTRAINT [FK_SystemParameters_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
        CONSTRAINT [FK_SystemParameters_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id]),
        
        -- Constraints
        CONSTRAINT [CHK_SystemParameters_DataType] CHECK ([DataType] IN ('int', 'string', 'bool', 'decimal', 'json', 'enum')),
        CONSTRAINT [CHK_SystemParameters_ParameterValue] CHECK (LEN([ParameterValue]) > 0)
    );
    
    PRINT '✓ Created table: SystemParameters';
END
ELSE
    PRINT '  Table SystemParameters already exists';
GO

-- ================================================================
-- INDEXES
-- ================================================================

-- Primary lookup by ParameterKey (most common query)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SystemParameters_ParameterKey' AND object_id = OBJECT_ID('dbo.SystemParameters'))
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX [IX_SystemParameters_ParameterKey]
    ON [dbo].[SystemParameters] ([ParameterKey])
    WHERE [IsActive] = 1;
    PRINT '✓ Created IX_SystemParameters_ParameterKey';
END
ELSE
    PRINT '  IX_SystemParameters_ParameterKey already exists';
GO

-- Category lookup (admin page filtering)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SystemParameters_Category' AND object_id = OBJECT_ID('dbo.SystemParameters'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SystemParameters_Category]
    ON [dbo].[SystemParameters] ([Category], [SubCategory])
    WHERE [IsActive] = 1;
    PRINT '✓ Created IX_SystemParameters_Category';
END
ELSE
    PRINT '  IX_SystemParameters_Category already exists';
GO

-- ================================================================
-- SEED DATA - Initial System Parameters
-- ================================================================

PRINT '';
PRINT '========================================';
PRINT 'Seeding Initial System Parameters';
PRINT '========================================';
GO

-- ================================================================
-- CATEGORY: Cache
-- PURPOSE: Cache durations and configurations
-- ================================================================

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Cache.Persoane.DurationMinutes')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [IsReadOnly])
    VALUES ('Cache.Persoane.DurationMinutes', 'Cache', 'Persoane', '5', 'int', '5', 'Cache duration in minutes for Persoane dropdown list', 'Persoane Cache Duration (min)', 0);
    PRINT '✓ Seeded: Cache.Persoane.DurationMinutes';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Cache.SizeLimit')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [IsReadOnly])
    VALUES ('Cache.SizeLimit', 'Cache', NULL, '1024', 'int', '1024', 'Maximum number of entries in memory cache', 'Cache Size Limit', 0);
    PRINT '✓ Seeded: Cache.SizeLimit';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Cache.CompactionPercentage')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [IsReadOnly])
    VALUES ('Cache.CompactionPercentage', 'Cache', NULL, '0.25', 'decimal', '0.25', 'Percentage of cache entries to remove when limit reached (0.25 = 25%)', 'Cache Compaction %', 0);
    PRINT '✓ Seeded: Cache.CompactionPercentage';
END
GO

-- ================================================================
-- CATEGORY: Validation
-- PURPOSE: Validation rules and limits
-- ================================================================

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Validation.Password.MinLength')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [MinValue], [MaxValue], [IsReadOnly])
    VALUES ('Validation.Password.MinLength', 'Validation', 'Password', '8', 'int', '8', 'Minimum password length for user accounts', 'Min Password Length', 6, 32, 0);
    PRINT '✓ Seeded: Validation.Password.MinLength';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Validation.Nume.MaxLength')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [MinValue], [MaxValue], [IsReadOnly])
    VALUES ('Validation.Nume.MaxLength', 'Validation', 'Persoane', '100', 'int', '100', 'Maximum length for Nume field', 'Max Nume Length', 1, 500, 0);
    PRINT '✓ Seeded: Validation.Nume.MaxLength';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Validation.Email.MaxLength')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [MinValue], [MaxValue], [IsReadOnly])
    VALUES ('Validation.Email.MaxLength', 'Validation', 'Common', '256', 'int', '256', 'Maximum length for email addresses', 'Max Email Length', 1, 500, 0);
    PRINT '✓ Seeded: Validation.Email.MaxLength';
END
GO

-- ================================================================
-- CATEGORY: Business
-- PURPOSE: Business rules and defaults
-- ================================================================

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Business.DefaultCountry')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [IsReadOnly])
    VALUES ('Business.DefaultCountry', 'Business', 'Persoane', 'Romania', 'string', 'Romania', 'Default country for new persons when Tara field is empty', 'Default Country', 0);
    PRINT '✓ Seeded: Business.DefaultCountry';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Business.Pagination.DefaultPageSize')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [MinValue], [MaxValue], [IsReadOnly])
    VALUES ('Business.Pagination.DefaultPageSize', 'Business', 'Grid', '20', 'int', '20', 'Default page size for grid pagination', 'Default Page Size', 10, 100, 0);
    PRINT '✓ Seeded: Business.Pagination.DefaultPageSize';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Business.Pagination.MaxPageSize')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [MinValue], [MaxValue], [IsReadOnly])
    VALUES ('Business.Pagination.MaxPageSize', 'Business', 'Grid', '1000', 'int', '1000', 'Maximum page size limit for GetAllSimpleAsync queries', 'Max Page Size', 100, 10000, 0);
    PRINT '✓ Seeded: Business.Pagination.MaxPageSize';
END
GO

-- ================================================================
-- CATEGORY: Session
-- PURPOSE: Session management configuration
-- ================================================================

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Session.TimeoutMinutes')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [MinValue], [MaxValue], [IsReadOnly])
    VALUES ('Session.TimeoutMinutes', 'Session', NULL, '30', 'int', '30', 'Session timeout duration in minutes', 'Session Timeout (min)', 5, 1440, 0);
    PRINT '✓ Seeded: Session.TimeoutMinutes';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Session.CleanupIntervalMinutes')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [MinValue], [MaxValue], [IsReadOnly])
    VALUES ('Session.CleanupIntervalMinutes', 'Session', NULL, '5', 'int', '5', 'Interval in minutes for expired session cleanup background job', 'Cleanup Interval (min)', 1, 60, 0);
    PRINT '✓ Seeded: Session.CleanupIntervalMinutes';
END
GO

-- ================================================================
-- CATEGORY: Performance
-- PURPOSE: Performance and connection settings
-- ================================================================

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Performance.QueryTimeout.Seconds')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [MinValue], [MaxValue], [IsReadOnly])
    VALUES ('Performance.QueryTimeout.Seconds', 'Performance', 'Database', '30', 'int', '30', 'Database query timeout in seconds', 'Query Timeout (sec)', 10, 300, 0);
    PRINT '✓ Seeded: Performance.QueryTimeout.Seconds';
END
GO

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Performance.SlowQueryThreshold.Milliseconds')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [MinValue], [MaxValue], [IsReadOnly])
    VALUES ('Performance.SlowQueryThreshold.Milliseconds', 'Performance', 'Logging', '500', 'int', '500', 'Threshold in milliseconds for logging slow queries', 'Slow Query Threshold (ms)', 100, 5000, 0);
    PRINT '✓ Seeded: Performance.SlowQueryThreshold.Milliseconds';
END
GO

-- ================================================================
-- CATEGORY: Enum
-- PURPOSE: Enum values (replacing C# enums with DB-driven configuration)
-- ================================================================

IF NOT EXISTS (SELECT * FROM [dbo].[SystemParameters] WHERE [ParameterKey] = 'Enum.DataType.Values')
BEGIN
    INSERT INTO [dbo].[SystemParameters] ([ParameterKey], [Category], [SubCategory], [ParameterValue], [DataType], [DefaultValue], [Description], [DisplayName], [IsReadOnly])
    VALUES ('Enum.DataType.Values', 'Enum', 'SystemParameters', '["int","string","bool","decimal","json","enum"]', 'json', '["int","string","bool","decimal","json"]', 'Valid data types for system parameters', 'Parameter Data Types', 1);
    PRINT '✓ Seeded: Enum.DataType.Values';
END
GO

-- ================================================================
-- SUMMARY
-- ================================================================

PRINT '';
PRINT '========================================';
PRINT 'System Parameters Summary';
PRINT '========================================';

SELECT 
    [Category],
    COUNT(*) AS ParameterCount,
    STRING_AGG([ParameterKey], ', ') AS Parameters
FROM [dbo].[SystemParameters]
WHERE [IsActive] = 1
GROUP BY [Category]
ORDER BY [Category];

PRINT '';
PRINT '✓ SystemParameters infrastructure created successfully';
PRINT '  - 1 table created';
PRINT '  - 2 indexes created';
PRINT '  - 12+ parameters seeded';
PRINT '';
PRINT '⚠️ IMPORTANT: Application restart required to load new parameters';
GO
