CREATE OR ALTER PROCEDURE core.usp_OrgTree_Get
    @UserId int
AS
BEGIN
    -- Arborele structurii organizatorice (02.01, 3.1, macheta 03), filtrat dupa drepturi: firmele cu Core.Company.View
    -- (cu punctele de lucru si locatiile lor) si grupurile care le contin. Cine are dreptul la nivel de instalare
    -- (atribuire cu CompanyId NULL) vede si grupurile fara firme. Include si nodurile inactive (filtrul e in interfata).
    SET NOCOUNT ON;

    CREATE TABLE #Companies (Id int NOT NULL PRIMARY KEY);
    INSERT INTO #Companies (Id)
    SELECT CompanyId FROM core.fn_UserCompanies(@UserId, N'Core.Company.View');

    DECLARE @InstallationWide bit = CASE WHEN EXISTS (
        SELECT 1
        FROM core.UserRoleAssignment AS a
        JOIN core.Role AS r ON r.Id = a.RoleId
        WHERE a.UserId = @UserId AND a.CompanyId IS NULL AND a.CompanyGroupId IS NULL
          AND ((r.Code = N'ADMIN' AND r.IsSystem = 1)
               OR EXISTS (SELECT 1 FROM core.RolePermission AS rp JOIN core.Permission AS p ON p.Id = rp.PermissionId
                          WHERE rp.RoleId = a.RoleId AND p.Code = N'Core.Company.View'))) THEN 1 ELSE 0 END;

    WITH VisibleGroups AS (
        SELECT g.Id, g.ParentGroupId
        FROM core.CompanyGroup AS g
        WHERE @InstallationWide = 1
           OR EXISTS (SELECT 1 FROM core.Company AS c JOIN #Companies AS v ON v.Id = c.Id WHERE c.GroupId = g.Id)
        UNION ALL
        SELECT p.Id, p.ParentGroupId
        FROM core.CompanyGroup AS p
        JOIN VisibleGroups AS v ON v.ParentGroupId = p.Id
        WHERE @InstallationWide = 0
    )
    SELECT g.Id, g.Code, g.Name, g.ParentGroupId, g.ParentCompanyId, g.IsActive, g.RowVersion
    FROM core.CompanyGroup AS g
    WHERE g.Id IN (SELECT Id FROM VisibleGroups)
    ORDER BY g.Name, g.Id;

    SELECT c.Id, c.GroupId, c.Code, c.Name, c.FiscalCode, c.VatPayer, c.IsActive
    FROM core.Company AS c
    JOIN #Companies AS v ON v.Id = c.Id
    ORDER BY c.Name, c.Id;

    SELECT b.Id, b.CompanyId, b.Code, b.Name, b.Address, b.City, b.CountyCode, b.CountryCode, b.PostalCode, b.IsActive, b.RowVersion, b.Phone, b.Email
    FROM core.Branch AS b
    JOIN #Companies AS v ON v.Id = b.CompanyId
    ORDER BY b.Name, b.Id;

    SELECT l.Id, l.CompanyId, l.BranchId, l.Code, l.Name, l.LocationType, l.Address, l.City, l.CountyCode, l.CountryCode,
           l.PostalCode, l.IsActive, l.RowVersion, l.Phone, l.Email
    FROM core.Location AS l
    JOIN #Companies AS v ON v.Id = l.CompanyId
    ORDER BY l.Name, l.Id;

    SELECT k.Id, k.CompanyId, k.Name, k.Role, k.Phone, k.Email, k.Notes, k.IsActive, k.RowVersion, k.BranchId, k.LocationId
    FROM core.CompanyContact AS k
    JOIN #Companies AS v ON v.Id = k.CompanyId
    WHERE k.BranchId IS NOT NULL OR k.LocationId IS NOT NULL
    ORDER BY k.IsActive DESC, k.Name, k.Id;
END;
