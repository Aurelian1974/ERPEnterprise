CREATE OR ALTER PROCEDURE core.usp_DefaultDimension_List
    @CompanyId  int,
    @SourceType tinyint,
    @SourceId   int
AS
BEGIN
    -- Dimensiunile implicite ale unui partener (1), articol (2) sau locatii (3) pe firma (02.01, 3.6 punctul 14): tipurile active,
    -- fiecare cu regula si valoarea data (randurile fara regula salvata au ValuePosting NULL), si valorile active ale firmei.
    SET NOCOUNT ON;

    SELECT t.Code AS DimensionType, t.Name AS TypeName, d.DimensionValueId, d.ValuePosting
    FROM core.DimensionType AS t
    LEFT JOIN core.DefaultDimension AS d ON d.DimensionType = t.Code AND d.CompanyId = @CompanyId AND d.SourceType = @SourceType AND d.SourceId = @SourceId
    WHERE t.IsActive = 1 OR d.Id IS NOT NULL
    ORDER BY t.SortOrder, t.Code;

    SELECT v.Id, v.DimensionType, v.Code, v.Name
    FROM core.DimensionValue AS v
    WHERE v.CompanyId = @CompanyId AND (v.IsActive = 1 OR v.Id IN (SELECT d.DimensionValueId FROM core.DefaultDimension AS d
        WHERE d.CompanyId = @CompanyId AND d.SourceType = @SourceType AND d.SourceId = @SourceId))
    ORDER BY v.DimensionType, v.CodeKey;
END;
