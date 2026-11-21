CREATE OR ALTER PROCEDURE core.usp_User_Get
    @Id int
AS
BEGIN
    -- Fisa utilizatorului (machetele 06b, 06c): contul, angajatul legat sau organizatia externa, starea de securitate din ids
    -- (fara hash-uri) si atribuirile de roluri. Contul tehnic „system” (Id = 1) nu se deschide.
    SET NOCOUNT ON;

    SELECT u.Id, u.SubjectId, u.UserName, u.DisplayName, u.FirstName, u.LastName, u.Email, u.Phone, u.IsActive, u.IsSystem, u.IsEmployee,
           u.EmployeeId, e.EmployeeNo, e.CompanyId AS EmployeeCompanyId, ec.Name AS EmployeeCompanyName, b.Name AS EmployeeBranchName,
           e.Department AS EmployeeDepartment, e.JobTitle AS EmployeeJobTitle, e.Phone AS EmployeePhone, e.HireDate AS EmployeeHireDate,
           e.LeaveDate AS EmployeeLeaveDate, e.IsActive AS EmployeeIsActive,
           u.ExternalAccessType, u.ExternalPartnerId, p.Name AS ExternalPartnerName, p.FiscalCode AS ExternalPartnerFiscalCode,
           u.SponsorUserId, sp.DisplayName AS SponsorName, u.AccessReason, u.ValidFrom, u.ValidUntil,
           u.Language, u.LastCompanyId, u.LastLoginAt, u.CreatedAt, cu.DisplayName AS CreatedByName,
           COALESCE(u.ModifiedAt, u.CreatedAt) AS LastChangedAt, COALESCE(mu.DisplayName, cu.DisplayName) AS ModifiedByName,
           s.HasPassword, s.PasswordChangedAt, s.MustChangePassword, s.TwoFactorEnabled, s.TwoFactorRequired, s.LockedUntil,
           s.ActiveSessions, CAST(CASE WHEN s.SubjectId IS NULL THEN 0 ELSE 1 END AS bit) AS HasAccount,
           u.RowVersion
    FROM core.[User] AS u
    LEFT JOIN core.Employee AS e ON e.Id = u.EmployeeId
    LEFT JOIN core.Company AS ec ON ec.Id = e.CompanyId
    LEFT JOIN core.Branch AS b ON b.Id = e.BranchId
    LEFT JOIN core.Partner AS p ON p.Id = u.ExternalPartnerId
    LEFT JOIN core.[User] AS sp ON sp.Id = u.SponsorUserId
    JOIN core.[User] AS cu ON cu.Id = u.CreatedBy
    LEFT JOIN core.[User] AS mu ON mu.Id = u.ModifiedBy
    LEFT JOIN ids.v_UserAccountSecurity AS s ON s.SubjectId = u.SubjectId
    WHERE u.Id = @Id AND u.Id <> 1;

    SELECT a.Id, a.RoleId, r.Code AS RoleCode, r.Name AS RoleName, a.CompanyId, c.Name AS CompanyName, a.DimensionScope
    FROM core.UserRoleAssignment AS a
    JOIN core.Role AS r ON r.Id = a.RoleId
    LEFT JOIN core.Company AS c ON c.Id = a.CompanyId
    WHERE a.UserId = @Id AND @Id <> 1
    ORDER BY CASE WHEN a.CompanyId IS NULL THEN 0 ELSE 1 END, c.Name, r.Name;
END;
