CREATE OR ALTER PROCEDURE core.usp_UserLookups_Get
AS
BEGIN
    -- Listele fisei utilizatorului (02.01, 11): rolurile, firmele active si responsabilii interni posibili pentru externi
    -- (utilizatori activi angajati sau conturi de sistem, fara contul tehnic „system”).
    SET NOCOUNT ON;

    SELECT Id, Code, Name, IsSystem
    FROM core.Role
    ORDER BY CASE WHEN IsSystem = 1 THEN 0 ELSE 1 END, Name;

    SELECT Id, Code, Name
    FROM core.Company
    WHERE IsActive = 1
    ORDER BY Name;

    SELECT Id, DisplayName
    FROM core.[User]
    WHERE IsActive = 1 AND Id <> 1 AND (IsEmployee = 1 OR IsSystem = 1)
    ORDER BY NameSearch, Id;
END;
