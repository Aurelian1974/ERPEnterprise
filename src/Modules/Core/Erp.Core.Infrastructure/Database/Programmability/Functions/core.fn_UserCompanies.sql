CREATE OR ALTER FUNCTION core.fn_UserCompanies
(
    @UserId     int,
    @Permission nvarchar(100) = NULL
)
RETURNS TABLE
AS
RETURN
    -- Firmele pe care utilizatorul are permisiunea (NULL = orice rol pe firma), dupa regulile din 02.01, 3.4 si 8: o atribuire
    -- pe instalare, pe grup, pe firma, pe un punct de lucru sau pe o locatie a firmei (core.fn_AssignmentScope); rolul de sistem
    -- ADMIN are toate permisiunile.
    SELECT DISTINCT s.CompanyId
    FROM core.fn_AssignmentScope(@UserId) AS s
    WHERE @Permission IS NULL
       OR s.IsAdmin = 1
       OR EXISTS (
            SELECT 1
            FROM core.RolePermission AS rp
            JOIN core.Permission AS p ON p.Id = rp.PermissionId
            WHERE rp.RoleId = s.RoleId AND p.Code = @Permission);
