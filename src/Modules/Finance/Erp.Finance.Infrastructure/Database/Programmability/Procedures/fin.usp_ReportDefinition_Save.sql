CREATE OR ALTER PROCEDURE fin.usp_ReportDefinition_Save
    @CompanyId   int,
    @Id          int = NULL,
    @RowVersion  binary(8) = NULL,
    @Code        varchar(30),
    @Name        nvarchar(200),
    @ChartId     int = NULL,
    @Rows        nvarchar(max),
    @UserId      int
AS
BEGIN
    -- Un raport al clientului (C5 3.5, decizia 4): creat sau modificat cu randurile lui (formulele sunt validate de aplicatie inainte). Rapoartele
    -- livrate nu se modifica (51975). Intoarce Id-ul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    IF @Id IS NULL
    BEGIN
        INSERT INTO fin.ReportDefinition (Code, Name, Kind, CompanyId, ChartId, CreatedBy) VALUES (@Code, @Name, 2, @CompanyId, @ChartId, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @Kind tinyint, @Version binary(8);
        SELECT @Kind = Kind, @Version = RowVersion FROM fin.ReportDefinition WITH (UPDLOCK) WHERE Id = @Id AND (CompanyId = @CompanyId OR CompanyId IS NULL);
        IF @Kind IS NULL
            THROW 51971, N'Raportul nu există.', 1;
        IF @Kind = 1
            THROW 51975, N'Rapoartele livrate nu se modifică.', 1;
        IF @Version <> @RowVersion
            THROW 51972, N'Raportul a fost modificat între timp.', 1;
        UPDATE fin.ReportDefinition SET Code = @Code, Name = @Name, ChartId = @ChartId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
        DELETE FROM fin.ReportRow WHERE DefinitionId = @Id;
    END;
    INSERT INTO fin.ReportRow (DefinitionId, RowCode, Label, Formula, IsTotal, Indent, SortOrder)
    SELECT @Id, RowCode, Label, NULLIF(Formula, N''), ISNULL(IsTotal, 0), ISNULL(Indent, 0), CAST(x.[key] AS int) + 1
    FROM OPENJSON(@Rows) AS x CROSS APPLY OPENJSON(x.value) WITH (RowCode varchar(30) '$.rowCode', Label nvarchar(300) '$.label', Formula nvarchar(1000) '$.formula',
        IsTotal bit '$.isTotal', Indent tinyint '$.indent') AS r;
    COMMIT;
    SELECT @Id;
END;
