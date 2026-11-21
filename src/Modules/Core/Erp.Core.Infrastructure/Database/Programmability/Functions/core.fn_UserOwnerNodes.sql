CREATE OR ALTER FUNCTION core.fn_UserOwnerNodes
(
    @UserId     int,
    @CompanyId  int,
    @Permission nvarchar(100),
    @Manage     bit
)
RETURNS TABLE
AS
RETURN
    -- Nodurile firmei ale caror articole / parteneri proprii ii sunt accesibile utilizatorului (02.01, 3.2-3.4; 21.11.2026):
    -- WholeCompany = 1: toata firma. Altfel cate un rand pe nod: punctul de lucru (BranchId), locatia (LocationId) sau firma insasi
    -- (ambele NULL, doar la vizualizare). La vizualizare (@Manage = 0) intra si nodurile de deasupra (firma, punctul de lucru al
    -- locatiei); la modificare doar nodul lui si cele de dedesubt (locatiile punctului de lucru).
    WITH Access AS (
        SELECT n.WholeCompany, n.BranchId, n.LocationId, n.LocationBranchId
        FROM core.fn_UserNodeAccess(@UserId, @CompanyId, @Permission) AS n
    )
    SELECT CAST(1 AS bit) AS WholeCompany, CAST(NULL AS int) AS BranchId, CAST(NULL AS int) AS LocationId
    WHERE EXISTS (SELECT 1 FROM Access WHERE WholeCompany = 1)
    UNION
    SELECT CAST(0 AS bit), x.BranchId, x.LocationId
    FROM (
        SELECT a.BranchId, CAST(NULL AS int) AS LocationId FROM Access AS a WHERE a.BranchId IS NOT NULL
        UNION
        SELECT CAST(NULL AS int), l.Id FROM Access AS a JOIN core.Location AS l ON l.BranchId = a.BranchId
        UNION
        SELECT CAST(NULL AS int), a.LocationId FROM Access AS a WHERE a.LocationId IS NOT NULL
        UNION
        SELECT a.LocationBranchId, CAST(NULL AS int) FROM Access AS a WHERE @Manage = 0 AND a.LocationBranchId IS NOT NULL
        UNION
        SELECT CAST(NULL AS int), CAST(NULL AS int) FROM Access AS a WHERE @Manage = 0
    ) AS x
    WHERE NOT EXISTS (SELECT 1 FROM Access WHERE WholeCompany = 1);
