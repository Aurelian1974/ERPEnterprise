CREATE OR ALTER PROCEDURE core.usp_UserRoleAssignment_Save
    @UserId      int,
    @Assignments core.UserRoleAssignmentList READONLY,
    @ModifiedBy  int
AS
BEGIN
    -- Rolurile unui utilizator (02.01, 3.4; tab-ul „Roluri și firme”), salvate impreuna in tranzactia comenzii: lista primita
    -- inlocuieste atribuirile existente; cele pastrate isi pastreaza restrictiile pe dimensiuni. Un rol se atribuie pe un nod
    -- (deciziile din 21.11.2026): instalare (toate campurile NULL, doar rolul de sistem ADMIN: 50034), grup de firme
    -- (CompanyGroupId, orice rol), firma, punct de lucru sau locatie (firma se deduce din nod). Ultimul administrator activ al
    -- instalarii nu-si pierde rolul (50015). Intoarce 1 daca utilizatorul are dupa salvare rolul ADMIN (2FA obligatoriu).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @UserId = 1
        THROW 50033, N'Contul tehnic „system” nu primește roluri.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.[User] WITH (UPDLOCK, ROWLOCK) WHERE Id = @UserId)
        THROW 50027, N'Utilizatorul nu există.', 1;

    -- Nodul normalizat: firma punctului de lucru sau a locatiei.
    DECLARE @Scope TABLE (
        RoleId int NOT NULL, CompanyGroupId int NULL, CompanyId int NULL, BranchId int NULL, LocationId int NULL, Valid bit NOT NULL);
    INSERT INTO @Scope (RoleId, CompanyGroupId, CompanyId, BranchId, LocationId, Valid)
    SELECT x.RoleId, x.CompanyGroupId,
           COALESCE(b.CompanyId, l.CompanyId, x.CompanyId),
           x.BranchId, x.LocationId,
           CAST(CASE
                WHEN x.CompanyGroupId IS NOT NULL AND (x.CompanyId IS NOT NULL OR x.BranchId IS NOT NULL OR x.LocationId IS NOT NULL) THEN 0
                WHEN x.BranchId IS NOT NULL AND x.LocationId IS NOT NULL THEN 0
                WHEN x.CompanyGroupId IS NOT NULL AND g.Id IS NULL THEN 0
                WHEN x.CompanyId IS NOT NULL AND c.Id IS NULL THEN 0
                WHEN x.BranchId IS NOT NULL AND (b.Id IS NULL OR (x.CompanyId IS NOT NULL AND x.CompanyId <> b.CompanyId)) THEN 0
                WHEN x.LocationId IS NOT NULL AND (l.Id IS NULL OR (x.CompanyId IS NOT NULL AND x.CompanyId <> l.CompanyId)) THEN 0
                ELSE 1 END AS bit)
    FROM @Assignments AS x
    LEFT JOIN core.CompanyGroup AS g ON g.Id = x.CompanyGroupId
    LEFT JOIN core.Company AS c ON c.Id = x.CompanyId
    LEFT JOIN core.Branch AS b ON b.Id = x.BranchId
    LEFT JOIN core.Location AS l ON l.Id = x.LocationId;

    IF EXISTS (SELECT 1 FROM @Scope AS x WHERE NOT EXISTS (SELECT 1 FROM core.Role AS r WHERE r.Id = x.RoleId))
        THROW 50021, N'Rolul ales nu există.', 1;
    IF EXISTS (SELECT 1 FROM @Scope WHERE Valid = 0)
        THROW 50021, N'Grupul, firma, punctul de lucru sau locația aleasă nu există sau nu se potrivesc.', 1;
    IF EXISTS (SELECT 1 FROM @Scope AS x JOIN core.Role AS r ON r.Id = x.RoleId
               WHERE x.CompanyId IS NULL AND x.CompanyGroupId IS NULL AND NOT (r.Code = N'ADMIN' AND r.IsSystem = 1))
        THROW 50034, N'Doar rolul Administrator se poate atribui pentru toată instalarea; alegeți grupul, firma, punctul de lucru sau locația.', 1;
    IF EXISTS (SELECT RoleId FROM @Scope GROUP BY RoleId, CompanyGroupId, CompanyId, BranchId, LocationId HAVING COUNT(*) > 1)
        THROW 50021, N'Același rol apare de două ori pe același nod.', 1;

    -- Ultimul administrator activ la nivelul instalarii (A22).
    IF EXISTS (
            SELECT 1 FROM core.UserRoleAssignment AS a JOIN core.Role AS r ON r.Id = a.RoleId
            WHERE a.UserId = @UserId AND a.CompanyId IS NULL AND a.CompanyGroupId IS NULL AND r.Code = N'ADMIN' AND r.IsSystem = 1)
        AND NOT EXISTS (
            SELECT 1 FROM @Scope AS x JOIN core.Role AS r ON r.Id = x.RoleId
            WHERE x.CompanyId IS NULL AND x.CompanyGroupId IS NULL AND r.Code = N'ADMIN' AND r.IsSystem = 1)
        AND NOT EXISTS (
            SELECT 1
            FROM core.UserRoleAssignment AS a WITH (UPDLOCK, HOLDLOCK)
            JOIN core.Role AS r ON r.Id = a.RoleId
            JOIN core.v_UserLoginStatus AS s ON s.UserId = a.UserId
            WHERE a.UserId <> @UserId AND a.CompanyId IS NULL AND a.CompanyGroupId IS NULL AND r.Code = N'ADMIN' AND r.IsSystem = 1
              AND s.CanSignIn = 1)
        THROW 50015, N'Este ultimul administrator activ al instalării; atribuiți întâi rolul altui utilizator.', 1;

    DELETE a
    FROM core.UserRoleAssignment AS a
    WHERE a.UserId = @UserId
      AND NOT EXISTS (SELECT 1 FROM @Scope AS x
                      WHERE x.RoleId = a.RoleId
                        AND EXISTS (SELECT x.CompanyGroupId, x.CompanyId, x.BranchId, x.LocationId
                                    INTERSECT
                                    SELECT a.CompanyGroupId, a.CompanyId, a.BranchId, a.LocationId));

    INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyGroupId, CompanyId, BranchId, LocationId, DimensionScope, CreatedBy)
    SELECT @UserId, x.RoleId, x.CompanyGroupId, x.CompanyId, x.BranchId, x.LocationId, NULL, @ModifiedBy
    FROM @Scope AS x
    WHERE NOT EXISTS (SELECT 1 FROM core.UserRoleAssignment AS a
                      WHERE a.UserId = @UserId AND a.RoleId = x.RoleId
                        AND EXISTS (SELECT x.CompanyGroupId, x.CompanyId, x.BranchId, x.LocationId
                                    INTERSECT
                                    SELECT a.CompanyGroupId, a.CompanyId, a.BranchId, a.LocationId));

    SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM core.UserRoleAssignment AS a JOIN core.Role AS r ON r.Id = a.RoleId
            WHERE a.UserId = @UserId AND r.Code = N'ADMIN' AND r.IsSystem = 1) THEN 1 ELSE 0 END AS bit) AS IsAdmin;
END;
