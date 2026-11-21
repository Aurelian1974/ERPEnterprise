CREATE OR ALTER PROCEDURE core.usp_UserRoleAssignment_Save
    @UserId      int,
    @Assignments core.UserRoleAssignmentList READONLY,
    @ModifiedBy  int
AS
BEGIN
    -- Rolurile unui utilizator pe firme (02.01, 3.4; tab-ul „Roluri și firme”), salvate impreuna in tranzactia comenzii:
    -- lista primita inlocuieste atribuirile existente; cele pastrate isi pastreaza restrictiile pe dimensiuni.
    -- CompanyId NULL (toata instalarea) e permis doar rolului de sistem ADMIN (50034). Ultimul administrator activ al
    -- instalarii nu-si pierde rolul (50015). Intoarce 1 daca utilizatorul are dupa salvare rolul ADMIN (2FA obligatoriu).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @UserId = 1
        THROW 50033, N'Contul tehnic „system” nu primește roluri.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.[User] WITH (UPDLOCK, ROWLOCK) WHERE Id = @UserId)
        THROW 50027, N'Utilizatorul nu există.', 1;

    IF EXISTS (SELECT 1 FROM @Assignments AS x WHERE NOT EXISTS (SELECT 1 FROM core.Role AS r WHERE r.Id = x.RoleId))
        THROW 50021, N'Rolul ales nu există.', 1;
    IF EXISTS (SELECT 1 FROM @Assignments AS x WHERE x.CompanyId IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM core.Company AS c WHERE c.Id = x.CompanyId))
        THROW 50021, N'Firma aleasă nu există.', 1;
    IF EXISTS (SELECT 1 FROM @Assignments AS x JOIN core.Role AS r ON r.Id = x.RoleId
               WHERE x.CompanyId IS NULL AND NOT (r.Code = N'ADMIN' AND r.IsSystem = 1))
        THROW 50034, N'Doar rolul Administrator se poate atribui pentru toate firmele; alegeți firma.', 1;
    IF EXISTS (SELECT RoleId FROM @Assignments GROUP BY RoleId, CompanyId HAVING COUNT(*) > 1)
        THROW 50021, N'Același rol apare de două ori pe aceeași firmă.', 1;

    -- Ultimul administrator activ la nivelul instalarii (A22).
    IF EXISTS (
            SELECT 1 FROM core.UserRoleAssignment AS a JOIN core.Role AS r ON r.Id = a.RoleId
            WHERE a.UserId = @UserId AND a.CompanyId IS NULL AND r.Code = N'ADMIN' AND r.IsSystem = 1)
        AND NOT EXISTS (
            SELECT 1 FROM @Assignments AS x JOIN core.Role AS r ON r.Id = x.RoleId
            WHERE x.CompanyId IS NULL AND r.Code = N'ADMIN' AND r.IsSystem = 1)
        AND NOT EXISTS (
            SELECT 1
            FROM core.UserRoleAssignment AS a WITH (UPDLOCK, HOLDLOCK)
            JOIN core.Role AS r ON r.Id = a.RoleId
            JOIN core.v_UserLoginStatus AS s ON s.UserId = a.UserId
            WHERE a.UserId <> @UserId AND a.CompanyId IS NULL AND r.Code = N'ADMIN' AND r.IsSystem = 1 AND s.CanSignIn = 1)
        THROW 50015, N'Este ultimul administrator activ al instalării; atribuiți întâi rolul altui utilizator.', 1;

    DELETE a
    FROM core.UserRoleAssignment AS a
    WHERE a.UserId = @UserId
      AND NOT EXISTS (SELECT 1 FROM @Assignments AS x
                      WHERE x.RoleId = a.RoleId AND (x.CompanyId = a.CompanyId OR (x.CompanyId IS NULL AND a.CompanyId IS NULL)));

    INSERT INTO core.UserRoleAssignment (UserId, RoleId, CompanyId, DimensionScope, CreatedBy)
    SELECT @UserId, x.RoleId, x.CompanyId, NULL, @ModifiedBy
    FROM @Assignments AS x
    WHERE NOT EXISTS (SELECT 1 FROM core.UserRoleAssignment AS a
                      WHERE a.UserId = @UserId AND a.RoleId = x.RoleId
                        AND (a.CompanyId = x.CompanyId OR (a.CompanyId IS NULL AND x.CompanyId IS NULL)));

    SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM core.UserRoleAssignment AS a JOIN core.Role AS r ON r.Id = a.RoleId
            WHERE a.UserId = @UserId AND r.Code = N'ADMIN' AND r.IsSystem = 1) THEN 1 ELSE 0 END AS bit) AS IsAdmin;
END;
