CREATE OR ALTER FUNCTION core.fn_UserOwnerNodesInContext
(
    @UserId            int,
    @CompanyId         int,
    @Permission        nvarchar(100),
    @ContextBranchId   int,
    @ContextLocationId int
)
RETURNS TABLE
AS
RETURN
    -- Nodurile vizibile cand antetul are ales un punct de lucru sau o locatie (21.11.2026): ca acel nod ar fi singurul al
    -- utilizatorului (firma si nodurile de deasupra, nodul si cele de dedesubt). Contextul se accepta doar daca e in nodurile
    -- utilizatorului (rol pe toata firma, pe acel punct de lucru sau locatie, ori pe punctul de lucru al locatiei); altfel, sau fara
    -- context, raman nodurile lui (core.fn_UserOwnerNodes).
    WITH Access AS (
        SELECT n.WholeCompany, n.BranchId, n.LocationId FROM core.fn_UserNodeAccess(@UserId, @CompanyId, @Permission) AS n
    ),
    Context AS (
        SELECT l.BranchId AS ParentBranchId FROM core.Location AS l WHERE l.Id = @ContextLocationId AND l.CompanyId = @CompanyId
    ),
    Usable AS (
        SELECT 1 AS X
        WHERE (@ContextBranchId IS NOT NULL AND EXISTS (SELECT 1 FROM core.Branch AS b WHERE b.Id = @ContextBranchId AND b.CompanyId = @CompanyId)
               AND EXISTS (SELECT 1 FROM Access AS a WHERE a.WholeCompany = 1 OR a.BranchId = @ContextBranchId))
           OR (@ContextLocationId IS NOT NULL AND EXISTS (SELECT 1 FROM Context)
               AND EXISTS (SELECT 1 FROM Access AS a
                           WHERE a.WholeCompany = 1 OR a.LocationId = @ContextLocationId
                              OR a.BranchId = (SELECT c.ParentBranchId FROM Context AS c)))
    )
    SELECT o.WholeCompany, o.BranchId, o.LocationId
    FROM core.fn_UserOwnerNodes(@UserId, @CompanyId, @Permission, 0) AS o
    WHERE NOT EXISTS (SELECT 1 FROM Usable)
    UNION ALL
    SELECT CAST(0 AS bit), CAST(NULL AS int), CAST(NULL AS int) WHERE EXISTS (SELECT 1 FROM Usable)
    UNION ALL
    SELECT CAST(0 AS bit), @ContextBranchId, CAST(NULL AS int) WHERE EXISTS (SELECT 1 FROM Usable) AND @ContextBranchId IS NOT NULL
    UNION ALL
    SELECT CAST(0 AS bit), CAST(NULL AS int), l.Id
    FROM core.Location AS l
    WHERE EXISTS (SELECT 1 FROM Usable) AND @ContextBranchId IS NOT NULL AND l.BranchId = @ContextBranchId
    UNION ALL
    SELECT CAST(0 AS bit), CAST(NULL AS int), @ContextLocationId WHERE EXISTS (SELECT 1 FROM Usable) AND @ContextBranchId IS NULL AND @ContextLocationId IS NOT NULL
    UNION ALL
    SELECT CAST(0 AS bit), c.ParentBranchId, CAST(NULL AS int)
    FROM Context AS c
    WHERE EXISTS (SELECT 1 FROM Usable) AND @ContextBranchId IS NULL AND c.ParentBranchId IS NOT NULL;
