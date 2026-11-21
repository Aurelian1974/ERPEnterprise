CREATE OR ALTER FUNCTION core.fn_AssignmentScope
(
    @UserId int = NULL
)
RETURNS TABLE
AS
RETURN
    -- Atribuirile de roluri desfacute pe firmele pe care le acopera (02.01, 3.4; deciziile din 21.11.2026), pentru un utilizator
    -- sau pentru toti (@UserId NULL): instalarea = toate firmele; grupul = firmele grupului si ale subgrupurilor; firma = ea;
    -- punctul de lucru si locatia = firma lor, cu nodul in BranchId / LocationId (WholeCompany = 0). IsAdmin = rolul de sistem ADMIN.
    WITH GroupTree AS (
        SELECT g.Id AS RootGroupId, g.Id AS GroupId FROM core.CompanyGroup AS g
        UNION ALL
        SELECT t.RootGroupId, g.Id FROM GroupTree AS t JOIN core.CompanyGroup AS g ON g.ParentGroupId = t.GroupId
    )
    SELECT a.Id AS AssignmentId, a.UserId, a.RoleId, c.Id AS CompanyId, a.BranchId, a.LocationId,
           CAST(CASE WHEN a.BranchId IS NULL AND a.LocationId IS NULL THEN 1 ELSE 0 END AS bit) AS WholeCompany,
           CAST(CASE WHEN r.Code = N'ADMIN' AND r.IsSystem = 1 THEN 1 ELSE 0 END AS bit) AS IsAdmin
    FROM core.UserRoleAssignment AS a
    JOIN core.Role AS r ON r.Id = a.RoleId
    JOIN core.Company AS c
      ON c.Id = a.CompanyId
      OR (a.CompanyId IS NULL AND a.CompanyGroupId IS NULL)
      OR (a.CompanyGroupId IS NOT NULL AND c.GroupId IN (SELECT t.GroupId FROM GroupTree AS t WHERE t.RootGroupId = a.CompanyGroupId))
    WHERE @UserId IS NULL OR a.UserId = @UserId;
