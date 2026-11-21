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

    -- Nodurile pe care se atribuie un rol (21.11.2026): grupurile, punctele de lucru si locatiile active ale firmelor active.
    SELECT g.Id, g.Name, g.ParentGroupId
    FROM core.CompanyGroup AS g
    WHERE g.IsActive = 1
    ORDER BY g.Name;

    SELECT b.Id, b.CompanyId, b.Name
    FROM core.Branch AS b
    JOIN core.Company AS c ON c.Id = b.CompanyId AND c.IsActive = 1
    WHERE b.IsActive = 1
    ORDER BY b.Name;

    SELECT l.Id, l.CompanyId, l.BranchId, l.Name
    FROM core.Location AS l
    JOIN core.Company AS c ON c.Id = l.CompanyId AND c.IsActive = 1
    WHERE l.IsActive = 1
    ORDER BY l.Name;
END;
