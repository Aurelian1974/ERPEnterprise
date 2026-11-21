CREATE OR ALTER PROCEDURE core.usp_User_GetPermissions
    @SubjectId uniqueidentifier,
    @CompanyId int = NULL
AS
BEGIN
    -- Utilizatorul activ cu SubjectId-ul din cookie si permisiunile lui efective pe firma ceruta (02.01, 3.4 si 8).
    -- @CompanyId: firma din antetul X-Company-Id; 0 = nivelul instalarii (doar atribuirile cu CompanyId NULL);
    -- NULL = ultima firma a utilizatorului, altfel prima firma activa la care are acces.
    -- O firma ceruta la care utilizatorul nu are niciun rol (sau inactiva) nu se accepta: firma efectiva devine 0.
    -- Atribuirile pe instalare (ADMIN) si pe grup se aplica firmelor pe care le acopera; cele pe punct de lucru sau locatie, firmei lor.
    -- Utilizatorii externi sunt valabili doar intre ValidFrom si ValidUntil (inclusiv), angajatii pana la data incetarii.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @UserId int, @LastCompanyId int, @EffectiveCompanyId int;

    -- Aceeasi regula ca autentificarea (core.v_UserLoginStatus): activ, in perioada de valabilitate, contract neincetat.
    SELECT @UserId = u.UserId, @LastCompanyId = cu.LastCompanyId
    FROM core.v_UserLoginStatus AS u
    JOIN core.[User] AS cu ON cu.Id = u.UserId
    WHERE u.SubjectId = @SubjectId
      AND u.CanSignIn = 1;

    IF @CompanyId = 0
        SET @EffectiveCompanyId = 0;
    ELSE
    BEGIN
        SELECT TOP (1) @EffectiveCompanyId = c.Id
        FROM core.fn_UserCompanies(@UserId, NULL) AS f
        JOIN core.Company AS c ON c.Id = f.CompanyId
        WHERE c.IsActive = 1
          AND (c.Id = COALESCE(@CompanyId, @LastCompanyId) OR @CompanyId IS NULL)
        ORDER BY CASE WHEN c.Id = COALESCE(@CompanyId, @LastCompanyId) THEN 0 ELSE 1 END, c.Name, c.Id;

        SET @EffectiveCompanyId = COALESCE(@EffectiveCompanyId, 0);
    END;

    SELECT u.Id, u.UserName, u.DisplayName, u.Email, u.Language, @EffectiveCompanyId AS CompanyId, u.IsSystem, u.IsEmployee
    FROM core.[User] AS u
    WHERE u.Id = @UserId;

    -- Atribuirile care se aplica: pe firma, toate cele care o acopera (instalare, grup, firma, punct de lucru, locatie;
    -- core.fn_AssignmentScope); la nivelul instalarii (firma 0), doar cele pe instalare.
    DECLARE @Roles TABLE (RoleId int NOT NULL, IsAdmin bit NOT NULL);
    IF @EffectiveCompanyId = 0
        INSERT INTO @Roles (RoleId, IsAdmin)
        SELECT a.RoleId, CAST(CASE WHEN r.Code = N'ADMIN' AND r.IsSystem = 1 THEN 1 ELSE 0 END AS bit)
        FROM core.UserRoleAssignment AS a
        JOIN core.Role AS r ON r.Id = a.RoleId
        WHERE a.UserId = @UserId AND a.CompanyId IS NULL AND a.CompanyGroupId IS NULL;
    ELSE
        INSERT INTO @Roles (RoleId, IsAdmin)
        SELECT s.RoleId, s.IsAdmin FROM core.fn_AssignmentScope(@UserId) AS s WHERE s.CompanyId = @EffectiveCompanyId;

    -- Rolul de sistem ADMIN are drepturi depline (decizie 15.11.2026): toate permisiunile din catalog, inclusiv cele
    -- adaugate ulterior de alte module, fara sa depinda de core.RolePermission si fara exceptii pe utilizator. Pe un punct de
    -- lucru sau o locatie, datele raman limitate la nod (core.fn_UserNodeAccess).
    IF EXISTS (SELECT 1 FROM @Roles WHERE IsAdmin = 1)
    BEGIN
        SELECT p.Code FROM core.Permission AS p;
        RETURN;
    END;

    -- Drepturile rolurilor pe firma, plus exceptiile utilizatorului (21.11.2026): cele adaugate (G) se aduna, cele revocate (R)
    -- se scad si castiga. O exceptie cu CompanyId NULL se aplica pe toate firmele.
    WITH Granted AS (
        SELECT rp.PermissionId
        FROM @Roles AS a
        JOIN core.RolePermission AS rp ON rp.RoleId = a.RoleId
        UNION
        SELECT o.PermissionId
        FROM core.UserPermissionOverride AS o
        WHERE o.UserId = @UserId AND o.Effect = 'G'
          AND (o.CompanyId IS NULL OR o.CompanyId = @EffectiveCompanyId)
    )
    SELECT p.Code
    FROM Granted AS g
    JOIN core.Permission AS p ON p.Id = g.PermissionId
    WHERE NOT EXISTS (
        SELECT 1
        FROM core.UserPermissionOverride AS o
        WHERE o.UserId = @UserId AND o.PermissionId = g.PermissionId AND o.Effect = 'R'
          AND (o.CompanyId IS NULL OR o.CompanyId = @EffectiveCompanyId));
END;
