CREATE OR ALTER PROCEDURE core.usp_User_GetPermissions
    @SubjectId uniqueidentifier,
    @CompanyId int = NULL
AS
BEGIN
    -- Utilizatorul activ cu SubjectId-ul din cookie si permisiunile lui efective pe firma ceruta (02.01, 3.4 si 8).
    -- @CompanyId: firma din antetul X-Company-Id; 0 = nivelul instalarii (doar atribuirile cu CompanyId NULL);
    -- NULL = ultima firma a utilizatorului, altfel prima firma activa la care are acces.
    -- O firma ceruta la care utilizatorul nu are niciun rol (sau inactiva) nu se accepta: firma efectiva devine 0.
    -- Atribuirile cu CompanyId NULL (rolul ADMIN la nivel de instalare) se aplica oricarei firme.
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

    -- Rolul de sistem ADMIN are drepturi depline (decizie 15.11.2026): toate permisiunile din catalog, inclusiv cele
    -- adaugate ulterior de alte module, fara sa depinda de core.RolePermission.
    IF EXISTS (
        SELECT 1
        FROM core.UserRoleAssignment AS a
        JOIN core.Role AS r ON r.Id = a.RoleId
        WHERE a.UserId = @UserId
          AND r.Code = N'ADMIN' AND r.IsSystem = 1
          AND (a.CompanyId IS NULL OR a.CompanyId = @EffectiveCompanyId))
    BEGIN
        SELECT p.Code FROM core.Permission AS p;
        RETURN;
    END;

    SELECT DISTINCT p.Code
    FROM core.UserRoleAssignment AS a
    JOIN core.RolePermission AS rp ON rp.RoleId = a.RoleId
    JOIN core.Permission AS p ON p.Id = rp.PermissionId
    WHERE a.UserId = @UserId
      AND (a.CompanyId IS NULL OR a.CompanyId = @EffectiveCompanyId);
END;
