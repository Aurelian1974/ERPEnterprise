PRINT '=== Începere migrare 038_Articole_AddOwnership ===';

-- Add ownership columns to Articole table
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Articole') AND name = 'OwnerCompanyId')
BEGIN
    ALTER TABLE dbo.Articole
    ADD [OwnerCompanyId] UNIQUEIDENTIFIER NULL,
        [OwnerWorkPlaceId] UNIQUEIDENTIFIER NULL,
        [OwnerLocationId] UNIQUEIDENTIFIER NULL;

    PRINT '✅ Coloane ownership adăugate la tabela Articole';
END
ELSE
BEGIN
    PRINT 'ℹ️ Coloane ownership deja există în tabela Articole';
END

-- Add foreign key constraints
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Articole_OwnerCompanyId')
BEGIN
    ALTER TABLE dbo.Articole
    ADD CONSTRAINT [FK_Articole_OwnerCompanyId] FOREIGN KEY ([OwnerCompanyId]) REFERENCES [dbo].[Companies]([Id]);

    PRINT '✅ Foreign key FK_Articole_OwnerCompanyId adăugat';
END

-- Add indexes for performance
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Articole_OwnerCompanyId')
BEGIN
    CREATE INDEX [IX_Articole_OwnerCompanyId] ON [dbo].[Articole] ([OwnerCompanyId]) WHERE [OwnerCompanyId] IS NOT NULL;
    PRINT '✅ Index IX_Articole_OwnerCompanyId creat';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Articole_OwnerWorkPlaceId')
BEGIN
    CREATE INDEX [IX_Articole_OwnerWorkPlaceId] ON [dbo].[Articole] ([OwnerWorkPlaceId]) WHERE [OwnerWorkPlaceId] IS NOT NULL;
    PRINT '✅ Index IX_Articole_OwnerWorkPlaceId creat';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Articole_OwnerLocationId')
BEGIN
    CREATE INDEX [IX_Articole_OwnerLocationId] ON [dbo].[Articole] ([OwnerLocationId]) WHERE [OwnerLocationId] IS NOT NULL;
    PRINT '✅ Index IX_Articole_OwnerLocationId creat';
END

PRINT '=== Migrare 038_Articole_AddOwnership completă ===';