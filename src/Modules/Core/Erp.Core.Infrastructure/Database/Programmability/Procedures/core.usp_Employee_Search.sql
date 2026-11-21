CREATE OR ALTER PROCEDURE core.usp_Employee_Search
    @Search      nvarchar(200) = NULL,
    @CompanyId   int = NULL,
    @IsActive    bit = NULL,
    @WithoutUser bit = NULL,
    @Offset      int = 0,
    @PageSize    int = 50
AS
BEGIN
    -- Lista angajatilor (fisa minima din Nucleu, 02.01, 3.4): cautare prefix SARGable dupa prenume si nume sau nume si prenume
    -- (fara majuscule si diacritice) ori dupa marca; filtre pe firma, stare si „fara cont de utilizator” (alegerea angajatului
    -- in fisa utilizatorului). Al doilea rezultat: numarul angajatilor activi.
    SET NOCOUNT ON;

    SELECT e.Id, e.CompanyId, c.Name AS CompanyName, e.BranchId, b.Name AS BranchName, e.EmployeeNo, e.FirstName, e.LastName,
           e.Department, e.JobTitle, e.WorkEmail, e.HireDate, e.LeaveDate, e.IsActive, u.Id AS UserId, u.UserName,
           COUNT(*) OVER () AS TotalCount
    FROM core.Employee AS e
    JOIN core.Company AS c ON c.Id = e.CompanyId
    LEFT JOIN core.Branch AS b ON b.Id = e.BranchId
    LEFT JOIN core.[User] AS u ON u.EmployeeId = e.Id
    WHERE (@Search IS NULL
            OR e.NameSearch LIKE @Search + N'%'
            OR e.LastNameSearch LIKE @Search + N'%'
            OR e.EmployeeNoKey LIKE UPPER(@Search) + N'%')
      AND (@CompanyId IS NULL OR e.CompanyId = @CompanyId)
      AND (@IsActive IS NULL OR e.IsActive = @IsActive)
      AND (@WithoutUser IS NULL OR (@WithoutUser = 1 AND u.Id IS NULL) OR (@WithoutUser = 0 AND u.Id IS NOT NULL))
    ORDER BY e.LastNameSearch, e.Id
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);

    SELECT COUNT(*) AS ActiveCount FROM core.Employee WHERE IsActive = 1;
END;
