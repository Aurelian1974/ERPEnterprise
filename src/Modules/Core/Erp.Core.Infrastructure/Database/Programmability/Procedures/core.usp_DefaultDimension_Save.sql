CREATE OR ALTER PROCEDURE core.usp_DefaultDimension_Save
    @CompanyId  int,
    @SourceType tinyint,
    @SourceId   int,
    @Defaults   core.DefaultDimensionList READONLY,
    @UserId     int
AS
BEGIN
    -- Dimensiunile implicite ale sursei pe firma, salvate impreuna (lista inlocuieste regulile existente). Sursa exista (si, pentru
    -- locatie, e a firmei); valorile sunt ale firmei si de tipul randului (cheia straina compusa); regula fixa cere valoare.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF (@SourceType = 1 AND NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @SourceId))
       OR (@SourceType = 2 AND NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @SourceId))
       OR (@SourceType = 3 AND NOT EXISTS (SELECT 1 FROM core.Location WHERE Id = @SourceId AND CompanyId = @CompanyId))
        THROW 50027, N'Partenerul, articolul sau locația nu există.', 1;
    IF EXISTS (SELECT 1 FROM @Defaults AS d WHERE d.DimensionValueId IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM core.DimensionValue AS v WHERE v.Id = d.DimensionValueId AND v.CompanyId = @CompanyId AND v.DimensionType = d.DimensionType))
        THROW 50021, N'O valoare aleasă nu e a firmei sau nu e de tipul rândului.', 1;
    IF EXISTS (SELECT 1 FROM @Defaults WHERE ValuePosting = 3 AND DimensionValueId IS NULL)
        THROW 50021, N'Regula „fixă” cere o valoare.', 1;

    DELETE d FROM core.DefaultDimension AS d
    WHERE d.CompanyId = @CompanyId AND d.SourceType = @SourceType AND d.SourceId = @SourceId
      AND NOT EXISTS (SELECT 1 FROM @Defaults AS x WHERE x.DimensionType = d.DimensionType);

    MERGE core.DefaultDimension AS target
    USING @Defaults AS source
    ON target.CompanyId = @CompanyId AND target.SourceType = @SourceType AND target.SourceId = @SourceId AND target.DimensionType = source.DimensionType
    WHEN MATCHED AND (ISNULL(target.DimensionValueId, 0) <> ISNULL(source.DimensionValueId, 0) OR target.ValuePosting <> source.ValuePosting) THEN
        UPDATE SET DimensionValueId = source.DimensionValueId, ValuePosting = source.ValuePosting, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (CompanyId, SourceType, SourceId, DimensionType, DimensionValueId, ValuePosting, ModifiedAt, ModifiedBy)
        VALUES (@CompanyId, @SourceType, @SourceId, source.DimensionType, source.DimensionValueId, source.ValuePosting, SYSUTCDATETIME(), @UserId);
END;
