CREATE OR ALTER PROCEDURE core.usp_EmployeeLookups_Get
AS
BEGIN
    -- Listele fisei angajatului (02.01, 3.4): firmele si punctele lor de lucru active.
    SET NOCOUNT ON;

    SELECT Id, Code, Name
    FROM core.Company
    WHERE IsActive = 1
    ORDER BY Name;

    SELECT b.Id, b.CompanyId, b.Name
    FROM core.Branch AS b
    JOIN core.Company AS c ON c.Id = b.CompanyId
    WHERE b.IsActive = 1 AND c.IsActive = 1
    ORDER BY b.Name;
END;
