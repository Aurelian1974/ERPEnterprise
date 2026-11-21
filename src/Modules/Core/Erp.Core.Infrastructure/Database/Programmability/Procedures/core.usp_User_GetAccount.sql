CREATE OR ALTER PROCEDURE core.usp_User_GetAccount
    @Id int
AS
BEGIN
    -- Datele contului pentru actiunile de securitate si sincronizarea cu ids (02.01, 3.4): fara contul tehnic „system”.
    SET NOCOUNT ON;

    SELECT u.Id, u.SubjectId, u.UserName, u.Email, u.IsActive, u.IsSystem, u.IsEmployee, u.ExternalAccessType,
           CAST(CASE WHEN EXISTS (
                SELECT 1 FROM core.UserRoleAssignment AS a JOIN core.Role AS r ON r.Id = a.RoleId
                WHERE a.UserId = u.Id AND r.Code = N'ADMIN' AND r.IsSystem = 1) THEN 1 ELSE 0 END AS bit) AS IsAdmin,
           u.RowVersion
    FROM core.[User] AS u
    WHERE u.Id = @Id AND u.Id <> 1;
END;
