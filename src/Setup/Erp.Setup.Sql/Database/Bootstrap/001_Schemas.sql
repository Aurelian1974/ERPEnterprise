-- Bootstrap 001 (INSTALARE_ACTUALIZARE.md 8.3): schemele bazei, rulat la fiecare instalare, actualizare si reparare.
-- Idempotent; fiecare CREATE SCHEMA ruleaza intr-un apel sp_executesql separat (trebuie sa fie primul din lotul lui).
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Schemas TABLE (Ordinal int NOT NULL PRIMARY KEY, Name sysname NOT NULL);
INSERT INTO @Schemas (Ordinal, Name)
VALUES (1, N'core'), (2, N'ids'), (3, N'hangfire'), (4, N'intg'), (5, N'fin'), (6, N'inv'), (7, N'sales'), (8, N'purch'),
       (9, N'wms'), (10, N'tms'), (11, N'mfg'), (12, N'hr'), (13, N'pos'), (14, N'cons'), (15, N'crm'), (16, N'rpt');

DECLARE @Ordinal int = 0, @Name sysname, @Sql nvarchar(400);
WHILE 1 = 1
BEGIN
    SET @Name = NULL;
    SELECT TOP (1) @Ordinal = s.Ordinal, @Name = s.Name
    FROM @Schemas AS s
    WHERE s.Ordinal > @Ordinal
    ORDER BY s.Ordinal;

    IF @Name IS NULL
        BREAK;

    IF SCHEMA_ID(@Name) IS NULL
    BEGIN
        SET @Sql = N'CREATE SCHEMA ' + QUOTENAME(@Name) + N' AUTHORIZATION dbo;';
        EXEC sys.sp_executesql @Sql;
    END;
END;
