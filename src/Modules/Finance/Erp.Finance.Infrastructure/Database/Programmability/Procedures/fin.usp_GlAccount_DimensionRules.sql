CREATE OR ALTER PROCEDURE fin.usp_GlAccount_DimensionRules
    @AccountId int
AS
BEGIN
    -- Regulile de dimensiuni efective ale unui cont (C1 3.2, IChartOfAccountsQueries): a contului sau, lipsind, a celui mai apropiat parinte.
    SET NOCOUNT ON;

    WITH Ancestors AS (
        SELECT Id, ParentId, 0 AS Depth FROM fin.GlAccount WHERE Id = @AccountId
        UNION ALL
        SELECT g.Id, g.ParentId, x.Depth + 1 FROM fin.GlAccount AS g JOIN Ancestors AS x ON g.Id = x.ParentId)
    SELECT t.Code AS DimensionType, r.Mode
    FROM core.DimensionType AS t
    CROSS APPLY (SELECT TOP (1) d.Mode FROM Ancestors AS x JOIN fin.GlAccountDimensionRule AS d ON d.AccountId = x.Id AND d.DimensionType = t.Code
                 ORDER BY x.Depth) AS r
    ORDER BY t.SortOrder;
END;
