CREATE OR ALTER PROCEDURE core.usp_DimensionCombination_List
    @CompanyId int
AS
BEGIN
    -- Combinatiile blocate ale firmei (02.01, 3.6 punctul 14): doua tipuri care nu se folosesc impreuna sau o pereche de valori.
    SET NOCOUNT ON;

    SELECT c.Id, c.TypeA, ta.Name AS TypeAName, c.ValueAId, va.Code AS ValueACode, c.TypeB, tb.Name AS TypeBName, c.ValueBId, vb.Code AS ValueBCode,
           c.Note
    FROM core.DimensionCombination AS c
    JOIN core.DimensionType AS ta ON ta.Code = c.TypeA
    JOIN core.DimensionType AS tb ON tb.Code = c.TypeB
    LEFT JOIN core.DimensionValue AS va ON va.Id = c.ValueAId
    LEFT JOIN core.DimensionValue AS vb ON vb.Id = c.ValueBId
    WHERE c.CompanyId = @CompanyId
    ORDER BY ta.SortOrder, tb.SortOrder, va.CodeKey, vb.CodeKey;
END;
