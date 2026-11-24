CREATE OR ALTER PROCEDURE fin.usp_GlAccountDimensionRule_Save
    @AccountId  int,
    @Rules      nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Regulile de dimensiuni proprii ale contului (C1 3.2), inlocuite in bloc: JSON [{dimensionType, mode}] (0 interzis, 1 optional,
    -- 2 obligatoriu). Un cont nu face mai larga regula mostenita de la parinte (51813): obligatoriu ramane obligatoriu, interzis ramane interzis.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.GlAccount WHERE Id = @AccountId)
        THROW 51801, N'Contul nu există.', 1;

    DECLARE @New TABLE (DimensionType varchar(20) PRIMARY KEY, Mode tinyint);
    INSERT INTO @New (DimensionType, Mode)
    SELECT DimensionType, Mode FROM OPENJSON(@Rules) WITH (DimensionType varchar(20) '$.dimensionType', Mode tinyint '$.mode');

    IF EXISTS (SELECT 1 FROM @New AS n WHERE n.Mode NOT BETWEEN 0 AND 2 OR NOT EXISTS (SELECT 1 FROM core.DimensionType AS t WHERE t.Code = n.DimensionType))
        THROW 51021, N'Tipul de dimensiune sau modul nu există.', 1;

    WITH Ancestors AS (
        SELECT ParentId, 1 AS Depth FROM fin.GlAccount WHERE Id = @AccountId
        UNION ALL
        SELECT g.ParentId, x.Depth + 1 FROM fin.GlAccount AS g JOIN Ancestors AS x ON g.Id = x.ParentId WHERE x.ParentId IS NOT NULL)
    SELECT n.DimensionType INTO #Looser
    FROM @New AS n
    CROSS APPLY (SELECT TOP (1) d.Mode FROM Ancestors AS x JOIN fin.GlAccountDimensionRule AS d ON d.AccountId = x.ParentId AND d.DimensionType = n.DimensionType
                 ORDER BY x.Depth) AS p
    WHERE (p.Mode = 2 AND n.Mode <> 2) OR (p.Mode = 0 AND n.Mode <> 0);
    IF EXISTS (SELECT 1 FROM #Looser)
        THROW 51813, N'Contul nu poate face mai largă o regulă de dimensiune moștenită de la contul părinte.', 1;

    BEGIN TRANSACTION;
    DELETE FROM fin.GlAccountDimensionRule WHERE AccountId = @AccountId;
    INSERT INTO fin.GlAccountDimensionRule (AccountId, DimensionType, Mode, CreatedBy) SELECT @AccountId, DimensionType, Mode, @UserId FROM @New;
    COMMIT TRANSACTION;
END;
