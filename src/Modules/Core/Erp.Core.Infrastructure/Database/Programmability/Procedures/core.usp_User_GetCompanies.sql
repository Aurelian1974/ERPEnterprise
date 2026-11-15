CREATE OR ALTER PROCEDURE core.usp_User_GetCompanies
    @UserId int
AS
BEGIN
    -- Firmele active la care utilizatorul are un rol: selectorul de firma din antet (02.01, 11).
    SET NOCOUNT ON;

    SELECT c.Id, c.Code, c.Name
    FROM core.fn_UserCompanies(@UserId, NULL) AS f
    JOIN core.Company AS c ON c.Id = f.CompanyId
    WHERE c.IsActive = 1
    ORDER BY c.Name, c.Id;
END;
