-- Bootstrap 002 (INSTALARE_ACTUALIZARE.md 8.3): cate un rol de baza de date per schema, cu drepturi la nivel de schema
-- (acopera si obiectele create mai tarziu). Drepturile intre module se dau in Programmability/, nu aici. Idempotent.
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Schemas TABLE (Ordinal int NOT NULL PRIMARY KEY, Name sysname NOT NULL);
INSERT INTO @Schemas (Ordinal, Name)
VALUES (1, N'core'), (2, N'ids'), (3, N'hangfire'), (4, N'intg'), (5, N'fin'), (6, N'inv'), (7, N'sales'), (8, N'purch'),
       (9, N'wms'), (10, N'tms'), (11, N'mfg'), (12, N'hr'), (13, N'pos'), (14, N'cons'), (15, N'crm'), (16, N'rpt');

DECLARE @Ordinal int = 0, @Name sysname, @Role sysname, @Sql nvarchar(600);
WHILE 1 = 1
BEGIN
    SET @Name = NULL;
    SELECT TOP (1) @Ordinal = s.Ordinal, @Name = s.Name
    FROM @Schemas AS s
    WHERE s.Ordinal > @Ordinal
    ORDER BY s.Ordinal;

    IF @Name IS NULL
        BREAK;

    SET @Role = N'role_' + @Name;
    IF DATABASE_PRINCIPAL_ID(@Role) IS NULL
    BEGIN
        SET @Sql = N'CREATE ROLE ' + QUOTENAME(@Role) + N' AUTHORIZATION dbo;';
        EXEC sys.sp_executesql @Sql;
    END;

    SET @Sql = N'GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::' + QUOTENAME(@Name) + N' TO ' + QUOTENAME(@Role) + N';';
    EXEC sys.sp_executesql @Sql;
END;
