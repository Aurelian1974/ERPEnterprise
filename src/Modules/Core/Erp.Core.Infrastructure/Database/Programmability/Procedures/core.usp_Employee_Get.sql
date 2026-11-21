CREATE OR ALTER PROCEDURE core.usp_Employee_Get
    @Id int
AS
BEGIN
    -- Fisa minima a angajatului (02.01, 3.4) si contul de utilizator legat, daca exista.
    SET NOCOUNT ON;

    SELECT e.Id, e.CompanyId, c.Name AS CompanyName, e.BranchId, b.Name AS BranchName, e.EmployeeNo, e.FirstName, e.LastName,
           e.Department, e.JobTitle, e.WorkEmail, e.Phone, e.HireDate, e.LeaveDate, e.IsActive, u.Id AS UserId, u.UserName,
           e.CreatedAt, COALESCE(e.ModifiedAt, e.CreatedAt) AS LastChangedAt, COALESCE(mu.DisplayName, cu.DisplayName) AS ModifiedByName,
           e.RowVersion
    FROM core.Employee AS e
    JOIN core.Company AS c ON c.Id = e.CompanyId
    LEFT JOIN core.Branch AS b ON b.Id = e.BranchId
    LEFT JOIN core.[User] AS u ON u.EmployeeId = e.Id
    JOIN core.[User] AS cu ON cu.Id = e.CreatedBy
    LEFT JOIN core.[User] AS mu ON mu.Id = e.ModifiedBy
    WHERE e.Id = @Id;
END;
