CREATE OR ALTER FUNCTION core.fn_UserNodeAccess
(
    @UserId     int,
    @CompanyId  int,
    @Permission nvarchar(100)
)
RETURNS TABLE
AS
RETURN
    -- Unde are utilizatorul permisiunea in firma (02.01, 3.4; 21.11.2026): un rand cu WholeCompany = 1 pentru toata firma (rol pe
    -- instalare, grup sau firma, ori drept adaugat), altfel cate un rand pe punct de lucru (BranchId) sau locatie (LocationId).
    -- Un drept revocat (pe firma sau pe toate firmele) scoate permisiunea peste tot, cu exceptia rolului Administrator.
    -- LocationBranchId = punctul de lucru al locatiei, pentru regulile de vizibilitate ale proprietarului.
    WITH Revoked AS (
        SELECT 1 AS X
        FROM core.UserPermissionOverride AS o
        JOIN core.Permission AS p ON p.Id = o.PermissionId
        WHERE o.UserId = @UserId AND o.Effect = 'R' AND p.Code = @Permission
          AND (o.CompanyId IS NULL OR o.CompanyId = @CompanyId)
    ),
    Nodes AS (
        SELECT s.WholeCompany, s.BranchId, s.LocationId
        FROM core.fn_AssignmentScope(@UserId) AS s
        WHERE s.CompanyId = @CompanyId
          AND (s.IsAdmin = 1
               OR (NOT EXISTS (SELECT 1 FROM Revoked)
                   AND EXISTS (SELECT 1 FROM core.RolePermission AS rp JOIN core.Permission AS p ON p.Id = rp.PermissionId
                               WHERE rp.RoleId = s.RoleId AND p.Code = @Permission)))
        UNION ALL
        SELECT CAST(1 AS bit), NULL, NULL
        FROM core.UserPermissionOverride AS o
        JOIN core.Permission AS p ON p.Id = o.PermissionId
        WHERE o.UserId = @UserId AND o.Effect = 'G' AND p.Code = @Permission
          AND (o.CompanyId IS NULL OR o.CompanyId = @CompanyId)
          AND NOT EXISTS (SELECT 1 FROM Revoked)
          AND EXISTS (SELECT 1 FROM core.fn_AssignmentScope(@UserId) AS s WHERE s.CompanyId = @CompanyId)
    )
    SELECT n.WholeCompany, n.BranchId, n.LocationId, l.BranchId AS LocationBranchId
    FROM Nodes AS n
    LEFT JOIN core.Location AS l ON l.Id = n.LocationId;
