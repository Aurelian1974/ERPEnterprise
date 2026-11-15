CREATE OR ALTER FUNCTION core.fn_UserCompanies
(
    @UserId     int,
    @Permission nvarchar(100) = NULL
)
RETURNS TABLE
AS
RETURN
    -- Firmele pe care utilizatorul are permisiunea (NULL = orice rol pe firma), dupa regulile din 02.01, 3.4 si 8:
    -- atribuirile cu CompanyId NULL se aplica tuturor firmelor; rolul de sistem ADMIN are toate permisiunile.
    SELECT c.Id AS CompanyId
    FROM core.Company AS c
    WHERE EXISTS (
        SELECT 1
        FROM core.UserRoleAssignment AS a
        JOIN core.Role AS r ON r.Id = a.RoleId
        WHERE a.UserId = @UserId
          AND (a.CompanyId IS NULL OR a.CompanyId = c.Id)
          AND (@Permission IS NULL
               OR (r.Code = N'ADMIN' AND r.IsSystem = 1)
               OR EXISTS (
                   SELECT 1
                   FROM core.RolePermission AS rp
                   JOIN core.Permission AS p ON p.Id = rp.PermissionId
                   WHERE rp.RoleId = a.RoleId AND p.Code = @Permission)));
