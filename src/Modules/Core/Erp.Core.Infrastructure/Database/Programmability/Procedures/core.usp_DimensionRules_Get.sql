CREATE OR ALTER PROCEDURE core.usp_DimensionRules_Get
    @CompanyId  int,
    @PartnerId  int = NULL,
    @ItemId     int = NULL,
    @LocationId int = NULL
AS
BEGIN
    -- Pentru IDimensionRules (02.01, 6): regulile implicite ale partenerului, articolului si locatiei de pe document, pe firma, si
    -- combinatiile blocate ale firmei. Combinarea (articolul inaintea partenerului, apoi locatia) se face in aplicatie.
    SET NOCOUNT ON;

    SELECT d.SourceType, d.DimensionType, d.DimensionValueId, d.ValuePosting
    FROM core.DefaultDimension AS d
    JOIN core.DimensionType AS t ON t.Code = d.DimensionType AND t.IsActive = 1
    WHERE d.CompanyId = @CompanyId
      AND ((d.SourceType = 1 AND d.SourceId = @PartnerId) OR (d.SourceType = 2 AND d.SourceId = @ItemId) OR (d.SourceType = 3 AND d.SourceId = @LocationId));

    SELECT c.TypeA, c.ValueAId, c.TypeB, c.ValueBId
    FROM core.DimensionCombination AS c
    WHERE c.CompanyId = @CompanyId;
END;
