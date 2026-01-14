-- 033_Import_Judet_Localitate_TipLocalitate.sql
-- Import tables and data from ValyanMed into ValyanERP
-- Adds tables (if missing), imports rows (keeping original Ids and GUIDs), adds FK constraints and indexes
-- Runs inside a transaction; rolls back on error

SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    -- Create TipLocalitate if not exists
    IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name = 'TipLocalitate' AND s.name = 'dbo')
    BEGIN
        CREATE TABLE dbo.TipLocalitate (
            IdTipLocalitate INT NOT NULL PRIMARY KEY,
            CodTipLocalitate VARCHAR(10) NULL,
            DenumireTipLocalitate VARCHAR(100) NULL
        );
    END

    -- Create Judet if not exists
    IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name = 'Judet' AND s.name = 'dbo')
    BEGIN
        CREATE TABLE dbo.Judet (
            IdJudet INT NOT NULL PRIMARY KEY,
            JudetGuid UNIQUEIDENTIFIER NOT NULL,
            CodJudet NVARCHAR(10) NOT NULL,
            Nume NVARCHAR(100) NOT NULL,
            Siruta INT NULL,
            CodAuto NVARCHAR(5) NULL,
            Ordine INT NULL
        );
    END

    -- Create Localitate if not exists
    IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE t.name = 'Localitate' AND s.name = 'dbo')
    BEGIN
        CREATE TABLE dbo.Localitate (
            IdOras INT NOT NULL PRIMARY KEY,
            LocalitateGuid UNIQUEIDENTIFIER NOT NULL,
            IdJudet INT NOT NULL,
            Nume NVARCHAR(100) NOT NULL,
            Siruta INT NOT NULL,
            CodLocalitate VARCHAR(10) NOT NULL,
            IdTipLocalitate INT NULL
        );
    END

    -- Insert TipLocalitate rows (skip existing ids)
    INSERT INTO dbo.TipLocalitate (IdTipLocalitate, CodTipLocalitate, DenumireTipLocalitate)
    SELECT t.IdTipLocalitate, t.CodTipLocalitate, t.DenumireTipLocalitate
    FROM ValyanMed.dbo.TipLocalitate t
    WHERE NOT EXISTS (SELECT 1 FROM dbo.TipLocalitate dst WHERE dst.IdTipLocalitate = t.IdTipLocalitate);

    -- Insert Judet rows
    INSERT INTO dbo.Judet (IdJudet, JudetGuid, CodJudet, Nume, Siruta, CodAuto, Ordine)
    SELECT j.IdJudet, j.JudetGuid, j.CodJudet, j.Nume, j.Siruta, j.CodAuto, j.Ordine
    FROM ValyanMed.dbo.Judet j
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Judet dst WHERE dst.IdJudet = j.IdJudet);

    -- Insert Localitate rows (depends on Judet and TipLocalitate)
    INSERT INTO dbo.Localitate (IdOras, LocalitateGuid, IdJudet, Nume, Siruta, CodLocalitate, IdTipLocalitate)
    SELECT l.IdOras, l.LocalitateGuid, l.IdJudet, l.Nume, l.Siruta, l.CodLocalitate, l.IdTipLocalitate
    FROM ValyanMed.dbo.Localitate l
    WHERE NOT EXISTS (SELECT 1 FROM dbo.Localitate dst WHERE dst.IdOras = l.IdOras);

    -- Add indexes if not exist
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Localitate_Siruta' AND object_id = OBJECT_ID('dbo.Localitate'))
        CREATE INDEX IX_Localitate_Siruta ON dbo.Localitate(Siruta);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Localitate_CodLocalitate' AND object_id = OBJECT_ID('dbo.Localitate'))
        CREATE INDEX IX_Localitate_CodLocalitate ON dbo.Localitate(CodLocalitate);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Judet_Siruta' AND object_id = OBJECT_ID('dbo.Judet'))
        CREATE INDEX IX_Judet_Siruta ON dbo.Judet(Siruta);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Judet_CodJudet' AND object_id = OBJECT_ID('dbo.Judet'))
        CREATE INDEX IX_Judet_CodJudet ON dbo.Judet(CodJudet);

    -- Add foreign keys (if not exist)
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Localitate_Judet')
    BEGIN
        ALTER TABLE dbo.Localitate
        ADD CONSTRAINT FK_Localitate_Judet FOREIGN KEY (IdJudet) REFERENCES dbo.Judet(IdJudet);
    END

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Localitate_TipLocalitate')
    BEGIN
        ALTER TABLE dbo.Localitate
        ADD CONSTRAINT FK_Localitate_TipLocalitate FOREIGN KEY (IdTipLocalitate) REFERENCES dbo.TipLocalitate(IdTipLocalitate);
    END

    -- Return counts
    SELECT 'TipLocalitate' AS TableName, COUNT(*) AS Rows FROM dbo.TipLocalitate
    UNION ALL
    SELECT 'Judet', COUNT(*) FROM dbo.Judet
    UNION ALL
    SELECT 'Localitate', COUNT(*) FROM dbo.Localitate;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrNum INT = ERROR_NUMBER();
    RAISERROR('Import failed: %d %s', 16, 1, @ErrNum, @ErrMsg);
END CATCH
