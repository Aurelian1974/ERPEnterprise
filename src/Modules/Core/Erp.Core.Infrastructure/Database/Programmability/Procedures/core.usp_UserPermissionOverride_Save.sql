CREATE OR ALTER PROCEDURE core.usp_UserPermissionOverride_Save
    @CompanyId int = NULL,
    @UserIds   core.IdList READONLY,
    @Overrides core.UserPermissionOverrideList READONLY,
    @UserId    int
AS
BEGIN
    -- Exceptiile de drepturi ale utilizatorilor (02.01, 3.4 si 8; vederea „Roluri și utilizatori”, 21.11.2026): pentru fiecare
    -- utilizator din @UserIds, lista primita inlocuieste exceptiile lui pe firma @CompanyId (NULL = toate firmele). Exceptiile
    -- neschimbate isi pastreaza autorul si data. Un drept adaugat cere motivul (50036); utilizatorii cu rolul Administrator au deja
    -- toate drepturile si nu primesc exceptii (50036); contul tehnic „system” nu primeste drepturi (50033).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM @UserIds WHERE Id = 1)
        THROW 50033, N'Contul tehnic „system” nu primește drepturi.', 1;
    IF EXISTS (SELECT 1 FROM @UserIds AS x WHERE NOT EXISTS (SELECT 1 FROM core.[User] AS u WHERE u.Id = x.Id))
        THROW 50027, N'Utilizatorul nu există.', 1;
    IF @CompanyId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId)
        THROW 50021, N'Firma aleasă nu există.', 1;
    IF EXISTS (SELECT 1 FROM @Overrides AS o WHERE NOT EXISTS (SELECT 1 FROM @UserIds AS x WHERE x.Id = o.UserId))
        THROW 50021, N'O excepție aparține unui utilizator care nu e în salvare.', 1;
    IF EXISTS (SELECT 1 FROM @Overrides AS o WHERE NOT EXISTS (SELECT 1 FROM core.Permission AS p WHERE p.Id = o.PermissionId))
        THROW 50021, N'Un drept ales nu există.', 1;
    IF EXISTS (SELECT 1 FROM @Overrides WHERE Effect NOT IN ('G', 'R'))
        THROW 50021, N'Excepția este fie un drept adăugat, fie unul revocat.', 1;
    IF EXISTS (SELECT 1 FROM @Overrides WHERE Effect = 'G' AND (Reason IS NULL OR LEN(Reason) = 0))
        THROW 50036, N'Motivul este obligatoriu pentru un drept adăugat.', 1;
    IF EXISTS (
        SELECT 1
        FROM @UserIds AS x
        JOIN core.UserRoleAssignment AS a ON a.UserId = x.Id
        JOIN core.Role AS r ON r.Id = a.RoleId
        WHERE r.Code = N'ADMIN' AND r.IsSystem = 1
          AND (@CompanyId IS NULL OR a.CompanyId IS NULL OR a.CompanyId = @CompanyId)
          AND EXISTS (SELECT 1 FROM @Overrides AS o WHERE o.UserId = x.Id))
        THROW 50036, N'Un utilizator cu rolul Administrator are toate drepturile și nu primește excepții.', 1;

    DELETE o
    FROM core.UserPermissionOverride AS o
    JOIN @UserIds AS x ON x.Id = o.UserId
    WHERE (o.CompanyId = @CompanyId OR (o.CompanyId IS NULL AND @CompanyId IS NULL))
      AND NOT EXISTS (SELECT 1 FROM @Overrides AS n WHERE n.UserId = o.UserId AND n.PermissionId = o.PermissionId AND n.Effect = o.Effect);

    UPDATE o
    SET Reason = n.Reason
    FROM core.UserPermissionOverride AS o
    JOIN @Overrides AS n ON n.UserId = o.UserId AND n.PermissionId = o.PermissionId AND n.Effect = o.Effect
    WHERE (o.CompanyId = @CompanyId OR (o.CompanyId IS NULL AND @CompanyId IS NULL))
      AND (o.Reason <> n.Reason OR (o.Reason IS NULL AND n.Reason IS NOT NULL) OR (o.Reason IS NOT NULL AND n.Reason IS NULL));

    INSERT INTO core.UserPermissionOverride (UserId, PermissionId, CompanyId, Effect, Reason, CreatedBy)
    SELECT n.UserId, n.PermissionId, @CompanyId, n.Effect, n.Reason, @UserId
    FROM @Overrides AS n
    WHERE NOT EXISTS (SELECT 1 FROM core.UserPermissionOverride AS o
                      WHERE o.UserId = n.UserId AND o.PermissionId = n.PermissionId
                        AND (o.CompanyId = @CompanyId OR (o.CompanyId IS NULL AND @CompanyId IS NULL)));
END;
