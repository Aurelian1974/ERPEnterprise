CREATE OR ALTER PROCEDURE core.usp_OwnerOptions_Get
    @UserId           int,
    @CompanyId        int,
    @ViewPermission   nvarchar(100),
    @ManagePermission nvarchar(100)
AS
BEGIN
    -- Proprietarii posibili ai unui articol / partener in firma curenta (21.11.2026): firma (cu dreptul pe toata firma la
    -- vizualizare / modificare) si nodurile ei, fiecare cu CanView (filtrul listei) si CanManage (alegerea proprietarului).
    SET NOCOUNT ON;

    SELECT c.Id, c.Name,
           CAST(CASE WHEN EXISTS (SELECT 1 FROM core.fn_UserOwnerNodes(@UserId, @CompanyId, @ViewPermission, 0) WHERE WholeCompany = 1)
                THEN 1 ELSE 0 END AS bit) AS ViewWholeCompany,
           CAST(CASE WHEN EXISTS (SELECT 1 FROM core.fn_UserOwnerNodes(@UserId, @CompanyId, @ManagePermission, 1) WHERE WholeCompany = 1)
                THEN 1 ELSE 0 END AS bit) AS ManageWholeCompany
    FROM core.Company AS c
    WHERE c.Id = @CompanyId;

    WITH ViewNodes AS (SELECT * FROM core.fn_UserOwnerNodes(@UserId, @CompanyId, @ViewPermission, 0)),
         ManageNodes AS (SELECT * FROM core.fn_UserOwnerNodes(@UserId, @CompanyId, @ManagePermission, 1)),
         Nodes AS (
            SELECT b.Id AS BranchId, CAST(NULL AS int) AS LocationId, CAST(NULL AS int) AS ParentBranchId, b.Code, b.Name, b.IsActive
            FROM core.Branch AS b WHERE b.CompanyId = @CompanyId
            UNION ALL
            SELECT NULL, l.Id, l.BranchId, l.Code, l.Name, l.IsActive
            FROM core.Location AS l WHERE l.CompanyId = @CompanyId)
    SELECT n.BranchId, n.LocationId, n.ParentBranchId, n.Code, n.Name, n.IsActive,
           CAST(CASE WHEN EXISTS (SELECT 1 FROM ViewNodes AS v WHERE v.WholeCompany = 1 OR v.BranchId = n.BranchId OR v.LocationId = n.LocationId)
                THEN 1 ELSE 0 END AS bit) AS CanView,
           CAST(CASE WHEN n.IsActive = 1 AND EXISTS (SELECT 1 FROM ManageNodes AS m
                                                      WHERE m.WholeCompany = 1 OR m.BranchId = n.BranchId OR m.LocationId = n.LocationId)
                THEN 1 ELSE 0 END AS bit) AS CanManage
    FROM Nodes AS n
    ORDER BY COALESCE(n.ParentBranchId, n.BranchId, 0), CASE WHEN n.BranchId IS NOT NULL THEN 0 ELSE 1 END, n.Name;
END;
