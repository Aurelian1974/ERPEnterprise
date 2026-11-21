CREATE OR ALTER PROCEDURE core.usp_User_GetNodes
    @UserId    int,
    @CompanyId int
AS
BEGIN
    -- Selectorul „Punct de lucru / locație” din antet (21.11.2026): nodurile firmei curente pe care utilizatorul are un rol (oricare).
    -- Primul rezultat: rol pe toata firma (instalare, grup sau firma). Al doilea: punctele de lucru si locatiile active ale firmei
    -- (toate, cu rol pe firma; altfel punctele lui de lucru cu locatiile lor si locatiile lui).
    SET NOCOUNT ON;

    CREATE TABLE #Scope (WholeCompany bit NOT NULL, BranchId int NULL, LocationId int NULL);
    INSERT INTO #Scope (WholeCompany, BranchId, LocationId)
    SELECT s.WholeCompany, s.BranchId, s.LocationId FROM core.fn_AssignmentScope(@UserId) AS s WHERE s.CompanyId = @CompanyId;

    DECLARE @Whole bit = CASE WHEN EXISTS (SELECT 1 FROM #Scope WHERE WholeCompany = 1) THEN 1 ELSE 0 END;
    SELECT @Whole AS WholeCompany;

    SELECT x.BranchId, x.LocationId, x.ParentBranchId, x.Name
    FROM (
        SELECT b.Id AS BranchId, CAST(NULL AS int) AS LocationId, CAST(NULL AS int) AS ParentBranchId, b.Name
        FROM core.Branch AS b
        WHERE b.CompanyId = @CompanyId AND b.IsActive = 1
          AND (@Whole = 1 OR EXISTS (SELECT 1 FROM #Scope AS s WHERE s.BranchId = b.Id))
        UNION ALL
        SELECT NULL, l.Id, l.BranchId, l.Name
        FROM core.Location AS l
        WHERE l.CompanyId = @CompanyId AND l.IsActive = 1
          AND (@Whole = 1 OR EXISTS (SELECT 1 FROM #Scope AS s WHERE s.LocationId = l.Id OR (s.BranchId IS NOT NULL AND s.BranchId = l.BranchId)))
    ) AS x
    ORDER BY COALESCE(x.ParentBranchId, x.BranchId, 0), CASE WHEN x.BranchId IS NOT NULL THEN 0 ELSE 1 END, x.Name;
END;
