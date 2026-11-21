CREATE OR ALTER PROCEDURE core.usp_RoleMatrix_Get
    @CompanyId int = NULL
AS
BEGIN
    -- Configurare › Roluri (02.01, 8 si 11; macheta 07e si vederea „Roluri și utilizatori”, deciziile din 21.11.2026).
    -- @CompanyId: firma vederii „Roluri și utilizatori”; NULL = toate firmele. Intoarce, in ordine: catalogul de permisiuni,
    -- rolurile, permisiunile rolurilor, firmele active, utilizatorii cu roluri in vedere, rolurile lor in vedere si exceptiile.
    -- Pe o firma se vad exceptiile ei si cele pentru toate firmele; pe „toate firmele” doar cele pentru toate firmele.
    -- Un utilizator e in vedere daca are un rol pe un nod care acopera firma (instalare, grup, firma, punct de lucru, locatie).
    SET NOCOUNT ON;

    CREATE TABLE #Scope (UserId int NOT NULL, RoleId int NOT NULL, PRIMARY KEY (UserId, RoleId));
    INSERT INTO #Scope (UserId, RoleId)
    SELECT DISTINCT s.UserId, s.RoleId
    FROM core.fn_AssignmentScope(DEFAULT) AS s
    WHERE s.UserId <> 1 AND (@CompanyId IS NULL OR s.CompanyId = @CompanyId);
    -- Atribuirile pe instalare sau pe grup fara nicio firma inca se vad pe „toate firmele”.
    IF @CompanyId IS NULL
        INSERT INTO #Scope (UserId, RoleId)
        SELECT DISTINCT a.UserId, a.RoleId FROM core.UserRoleAssignment AS a
        WHERE a.UserId <> 1 AND NOT EXISTS (SELECT 1 FROM #Scope AS x WHERE x.UserId = a.UserId AND x.RoleId = a.RoleId);

    SELECT p.Id, p.Code, p.ModuleCode, p.Description
    FROM core.Permission AS p
    ORDER BY p.ModuleCode, p.Code;

    SELECT r.Id, r.Code, r.Name, r.Description, r.IsSystem,
           CAST(CASE WHEN r.Code = N'ADMIN' AND r.IsSystem = 1 THEN 1 ELSE 0 END AS bit) AS HasAllPermissions,
           (SELECT COUNT(DISTINCT a.UserId) FROM #Scope AS a WHERE a.RoleId = r.Id) AS UserCount,
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
      AND EXISTS (SELECT 1 FROM #Scope AS a WHERE a.UserId = u.Id)
    ORDER BY u.DisplayName, u.Id;

    SELECT a.UserId, a.RoleId FROM #Scope AS a;

    SELECT o.UserId, o.PermissionId, o.CompanyId, o.Effect, o.Reason
    FROM core.UserPermissionOverride AS o
    WHERE o.CompanyId IS NULL OR o.CompanyId = @CompanyId;

    -- Nodurile atribuirilor din vedere (21.11.2026): instalare, grup, firma, punct de lucru sau locatie.
    SELECT a.UserId, a.RoleId, a.CompanyGroupId, g.Name AS GroupName, a.CompanyId, c.Name AS CompanyName, a.BranchId, b.Name AS BranchName,
           a.LocationId, l.Name AS LocationName
    FROM core.UserRoleAssignment AS a
    LEFT JOIN core.CompanyGroup AS g ON g.Id = a.CompanyGroupId
    LEFT JOIN core.Company AS c ON c.Id = a.CompanyId
    LEFT JOIN core.Branch AS b ON b.Id = a.BranchId
    LEFT JOIN core.Location AS l ON l.Id = a.LocationId
    WHERE a.UserId <> 1
      AND (@CompanyId IS NULL
           OR EXISTS (SELECT 1 FROM core.fn_AssignmentScope(a.UserId) AS s WHERE s.AssignmentId = a.Id AND s.CompanyId = @CompanyId))
    ORDER BY a.UserId, g.Name, c.Name, b.Name, l.Name;
END;
