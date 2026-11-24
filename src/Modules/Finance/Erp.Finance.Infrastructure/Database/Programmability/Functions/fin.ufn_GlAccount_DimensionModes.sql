CREATE OR ALTER FUNCTION fin.ufn_GlAccount_DimensionModes (@AccountId int)
RETURNS TABLE
AS
RETURN
    -- Regulile de dimensiuni efective ale unui cont (C1 3.2): a contului sau, lipsind, a celui mai apropiat parinte; 0 interzis, 1 optional,
    -- 2 obligatoriu. Folosita la validarea liniilor de nota (C2 3.1 regula 8).
    WITH Ancestors AS (
        SELECT Id, ParentId, 0 AS Depth FROM fin.GlAccount WHERE Id = @AccountId
        UNION ALL
        SELECT g.Id, g.ParentId, x.Depth + 1 FROM fin.GlAccount AS g JOIN Ancestors AS x ON g.Id = x.ParentId)
    SELECT r.DimensionType, r.Mode
    FROM (SELECT d.DimensionType, d.Mode, ROW_NUMBER() OVER (PARTITION BY d.DimensionType ORDER BY x.Depth) AS Rn
          FROM Ancestors AS x JOIN fin.GlAccountDimensionRule AS d ON d.AccountId = x.Id) AS r
    WHERE r.Rn = 1;
