-- ================================================================
-- SCRIPT: 010_PerformanceIndexes.sql
-- PURPOSE: Add performance indexes for common queries
-- DATE: 2026-01-09
-- ================================================================

USE ValyanERP;
GO

PRINT '========================================';
PRINT 'Creating Performance Indexes';
PRINT '========================================';
GO

-- ================================================================
-- PERSOANE TABLE INDEXES
-- ================================================================

-- Index for email lookups (used in uniqueness validation)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Persoane_Email' AND object_id = OBJECT_ID('dbo.Persoane'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Persoane_Email]
    ON [dbo].[Persoane] ([Email])
    WHERE [Email] IS NOT NULL AND [IsActive] = 1;
    PRINT '✓ Created IX_Persoane_Email';
END
ELSE
    PRINT '  IX_Persoane_Email already exists';
GO

-- Index for CNP lookups (unique business identifier)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Persoane_CNP' AND object_id = OBJECT_ID('dbo.Persoane'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Persoane_CNP]
    ON [dbo].[Persoane] ([CNP])
    WHERE [CNP] IS NOT NULL AND [IsActive] = 1;
    PRINT '✓ Created IX_Persoane_CNP';
END
ELSE
    PRINT '  IX_Persoane_CNP already exists';
GO

-- Composite index for search queries (Nume + Prenume + IsActive)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Persoane_Search' AND object_id = OBJECT_ID('dbo.Persoane'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Persoane_Search]
    ON [dbo].[Persoane] ([IsActive], [Nume], [Prenume])
    INCLUDE ([Email], [CNP], [Telefon], [CreatedAt]);
    PRINT '✓ Created IX_Persoane_Search';
END
ELSE
    PRINT '  IX_Persoane_Search already exists';
GO

-- Index for sorting by CreatedAt (common in grids)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Persoane_CreatedAt' AND object_id = OBJECT_ID('dbo.Persoane'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Persoane_CreatedAt]
    ON [dbo].[Persoane] ([CreatedAt] DESC)
    WHERE [IsActive] = 1;
    PRINT '✓ Created IX_Persoane_CreatedAt';
END
ELSE
    PRINT '  IX_Persoane_CreatedAt already exists';
GO

-- ================================================================
-- USERS TABLE INDEXES (for custom Dapper UserStore)
-- ================================================================

-- Index for email lookups (authentication)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Users_Email' AND object_id = OBJECT_ID('dbo.Users'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Users_Email]
    ON [dbo].[Users] ([Email])
    WHERE [IsActive] = 1;
    PRINT '✓ Created IX_Users_Email';
END
ELSE
    PRINT '  IX_Users_Email already exists';
GO

-- Composite index for user searches (UserName + IsActive)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Users_Search' AND object_id = OBJECT_ID('dbo.Users'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Users_Search]
    ON [dbo].[Users] ([IsActive], [UserName])
    INCLUDE ([Email], [PersoanaId], [CreatedAt]);
    PRINT '✓ Created IX_Users_Search';
END
ELSE
    PRINT '  IX_Users_Search already exists';
GO

-- Index for PersoanaId foreign key lookups
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Users_PersoanaId' AND object_id = OBJECT_ID('dbo.Users'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Users_PersoanaId]
    ON [dbo].[Users] ([PersoanaId])
    WHERE [IsActive] = 1;
    PRINT '✓ Created IX_Users_PersoanaId';
END
ELSE
    PRINT '  IX_Users_PersoanaId already exists';
GO

-- ================================================================
-- SESSIONS TABLE INDEXES (for session management)
-- ================================================================

-- Index for UserId lookups (active sessions per user)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Sessions_UserId' AND object_id = OBJECT_ID('dbo.Sessions'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Sessions_UserId]
    ON [dbo].[Sessions] ([UserId], [IsActive])
    INCLUDE ([SessionId], [ExpiresAt], [CreatedAt]);
    PRINT '✓ Created IX_Sessions_UserId';
END
ELSE
    PRINT '  IX_Sessions_UserId already exists';
GO

-- Index for session cleanup (expired sessions)
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Sessions_ExpiresAt' AND object_id = OBJECT_ID('dbo.Sessions'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Sessions_ExpiresAt]
    ON [dbo].[Sessions] ([ExpiresAt])
    WHERE [IsActive] = 1;
    PRINT '✓ Created IX_Sessions_ExpiresAt';
END
ELSE
    PRINT '  IX_Sessions_ExpiresAt already exists';
GO

-- ================================================================
-- INDEX STATISTICS
-- ================================================================

PRINT '';
PRINT '========================================';
PRINT 'Index Creation Summary';
PRINT '========================================';

SELECT 
    t.name AS TableName,
    i.name AS IndexName,
    i.type_desc AS IndexType,
    COL_NAME(ic.object_id, ic.column_id) AS ColumnName,
    ic.is_included_column AS IsIncluded
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.tables t ON i.object_id = t.object_id
WHERE t.name IN ('Persoane', 'Users', 'Sessions')
  AND i.name LIKE 'IX_%'
ORDER BY t.name, i.name, ic.key_ordinal;

PRINT '';
PRINT '✓ Performance indexes created successfully';
PRINT '⚠️ IMPORTANT: Update statistics after deployment';
PRINT '   Run: EXEC sp_updatestats;';
GO
