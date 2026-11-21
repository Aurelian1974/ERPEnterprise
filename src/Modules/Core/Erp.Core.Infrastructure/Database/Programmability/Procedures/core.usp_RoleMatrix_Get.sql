CREATE OR ALTER PROCEDURE core.usp_RoleMatrix_Get
    @CompanyId int = NULL
AS
BEGIN
    -- Configurare › Roluri (02.01, 8 si 11; macheta 07e si vederea „Roluri și utilizatori”, deciziile din 21.11.2026).
    -- @CompanyId: firma vederii „Roluri și utilizatori”; NULL = toate firmele. Intoarce, in ordine: catalogul de permisiuni,
    -- rolurile, permisiunile rolurilor, firmele active, utilizatorii cu roluri in vedere, rolurile lor in vedere si exceptiile.
    -- Pe o firma se vad exceptiile ei si cele pentru toate firmele; pe „toate firmele” doar cele pentru toate firmele.
    SET NOCOUNT ON;

    SELECT p.Id, p.Code, p.ModuleCode, p.Description
    FROM core.Permission AS p
    ORDER BY p.ModuleCode, p.Code;

    SELECT r.Id, r.Code, r.Name, r.Description, r.IsSystem,
           CAST(CASE WHEN r.Code = N'ADMIN' AND r.IsSystem = 1 THEN 1 ELSE 0 END AS bit) AS HasAllPermissions,
           (SELECT COUNT(DISTINCT a.UserId) FROM core.UserRoleAssignment AS a
            WHERE a.RoleId = r.Id AND a.UserId <> 1
              AND (@CompanyId IS NULL OR a.CompanyId IS NULL OR a.CompanyId = @CompanyId)) AS UserCount,
           r.RowVersion
    FROM core.Role AS r
    ORDER BY r.IsSystem DESC, r.Name, r.Id;

    SELECT rp.RoleId, rp.PermissionId
    FROM core.RolePermission AS rp;

    SELECT c.Id, c.Code, c.Name
    FROM core.Company AS c
    WHERE c.IsActive = 1
    ORDER BY c.Name, c.Id;

    SELECT u.Id, u.DisplayName, u.UserName, u.IsActive
    FROM core.[User] AS u
    WHERE u.Id <> 1
      AND EXISTS (SELECT 1 FROM core.UserRoleAssignment AS a
                  WHERE a.UserId = u.Id AND (@CompanyId IS NULL OR a.CompanyId IS NULL OR a.CompanyId = @CompanyId))
    ORDER BY u.DisplayName, u.Id;

    SELECT DISTINCT a.UserId, a.RoleId
    FROM core.UserRoleAssignment AS a
    WHERE a.UserId <> 1
      AND (@CompanyId IS NULL OR a.CompanyId IS NULL OR a.CompanyId = @CompanyId);

    SELECT o.UserId, o.PermissionId, o.CompanyId, o.Effect, o.Reason
    FROM core.UserPermissionOverride AS o
    WHERE o.CompanyId IS NULL OR o.CompanyId = @CompanyId;
END;
