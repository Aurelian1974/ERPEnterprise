CREATE OR ALTER FUNCTION fin.ufn_SaftAccountCode (@AccountId int)
RETURNS TABLE
AS
RETURN
    -- Contul SAF-T al unui cont (C1, C4 3.5): al contului sau, la analitice, al celui mai apropiat cont parinte care il are.
    WITH chain AS (
        SELECT a.Id, a.ParentId, a.SaftAccountCode, 0 AS Depth FROM fin.GlAccount AS a WHERE a.Id = @AccountId
        UNION ALL
        SELECT p.Id, p.ParentId, p.SaftAccountCode, c.Depth + 1 FROM fin.GlAccount AS p JOIN chain AS c ON p.Id = c.ParentId WHERE c.SaftAccountCode IS NULL
    )
    SELECT TOP (1) SaftAccountCode FROM chain WHERE SaftAccountCode IS NOT NULL ORDER BY Depth;
