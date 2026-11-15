CREATE OR ALTER PROCEDURE core.usp_User_GetPermissions
    @SubjectId uniqueidentifier,
    @CompanyId int = NULL
AS
BEGIN
    -- Utilizatorul activ cu SubjectId-ul din cookie si permisiunile lui efective pe firma ceruta (02.01, 3.4 si 8).
    -- Fara firma ceruta se foloseste ultima firma a utilizatorului (11, selectorul de firma); 0 = nicio firma.
    -- Atribuirile cu CompanyId NULL (rolul ADMIN la nivel de instalare) se aplica oricarei firme.
    -- Utilizatorii externi sunt valabili doar pana la ValidUntil (inclusiv).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Today date = CAST(SYSUTCDATETIME() AS date);
    DECLARE @UserId int, @EffectiveCompanyId int;

    SELECT @UserId = u.Id, @EffectiveCompanyId = COALESCE(@CompanyId, u.LastCompanyId, 0)
    FROM core.[User] AS u
    WHERE u.SubjectId = @SubjectId
      AND u.IsActive = 1
      AND (u.ValidUntil IS NULL OR u.ValidUntil >= @Today);

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
