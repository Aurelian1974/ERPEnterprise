CREATE OR ALTER PROCEDURE core.usp_User_Search
    @Search    nvarchar(200) = NULL,
    @Kind      tinyint = NULL,
    @Filter    tinyint = NULL,
    @CompanyId int = NULL,
    @RoleId    int = NULL,
    @Today     date,
    @Offset    int = 0,
    @PageSize  int = 50
AS
BEGIN
    -- Lista utilizatorilor (macheta 06; 02.01, 11). Cautare prefix SARGable dupa numele afisat (fara majuscule si diacritice),
    -- numele de utilizator, email sau marca angajatului. @Kind: 1 angajati, 2 externi, 3 conturi de sistem.
    -- @Filter: 1 inactivi, 2 externi activi al caror acces expira in 60 de zile, 3 activi fara 2FA (conturile tehnice nu au 2FA).
    -- @CompanyId / @RoleId: au un rol pe firma (atribuirile la nivelul instalarii se aplica tuturor firmelor) / au rolul.
    -- Contul tehnic „system” (Id = 1) nu apare. Rezultate: pagina, atribuirile de roluri ale randurilor din pagina, indicatorii.
    SET NOCOUNT ON;

    DECLARE @Expiring date = DATEADD(DAY, 60, @Today);

    CREATE TABLE #Page (
        Id               int           NOT NULL PRIMARY KEY,
        SubjectId        uniqueidentifier NOT NULL,
        UserName         nvarchar(50)  COLLATE DATABASE_DEFAULT NOT NULL,
        DisplayName      nvarchar(200) COLLATE DATABASE_DEFAULT NOT NULL,
        Email            nvarchar(256) COLLATE DATABASE_DEFAULT NULL,
        IsActive         bit           NOT NULL,
        IsSystem         bit           NOT NULL,
        IsEmployee       bit           NOT NULL,
        EmployeeNo       nvarchar(20)  COLLATE DATABASE_DEFAULT NULL,
        EmployeeLeaveDate date         NULL,
        ExternalAccessType varchar(20) COLLATE DATABASE_DEFAULT NULL,
        OrganizationName nvarchar(200) COLLATE DATABASE_DEFAULT NULL,
        ValidUntil       date          NULL,
        TwoFactorEnabled bit           NULL,
        LastLoginAt      datetime2(3)  NULL,
        SortKey          nvarchar(200) COLLATE Latin1_General_100_CI_AI_SC_UTF8 NOT NULL,
        TotalCount       int           NOT NULL
    );

    INSERT INTO #Page (Id, SubjectId, UserName, DisplayName, Email, IsActive, IsSystem, IsEmployee, EmployeeNo, EmployeeLeaveDate,
        ExternalAccessType, OrganizationName, ValidUntil, TwoFactorEnabled, LastLoginAt, SortKey, TotalCount)
    SELECT u.Id, u.SubjectId, u.UserName, u.DisplayName, u.Email, u.IsActive, u.IsSystem, u.IsEmployee, e.EmployeeNo, e.LeaveDate,
           u.ExternalAccessType, p.Name, u.ValidUntil, s.TwoFactorEnabled, u.LastLoginAt, u.NameSearch, COUNT(*) OVER ()
    FROM core.[User] AS u
    LEFT JOIN core.Employee AS e ON e.Id = u.EmployeeId
    LEFT JOIN core.Partner AS p ON p.Id = u.ExternalPartnerId
    LEFT JOIN ids.v_UserAccountSecurity AS s ON s.SubjectId = u.SubjectId
    WHERE u.Id <> 1
      AND (@Search IS NULL
            OR u.NameSearch LIKE @Search + N'%'
            OR u.UserName LIKE LOWER(@Search) + N'%'
            OR u.Email LIKE LOWER(@Search) + N'%'
            OR e.EmployeeNoKey LIKE UPPER(@Search) + N'%')
      AND (@Kind IS NULL
            OR (@Kind = 1 AND u.IsEmployee = 1)
            OR (@Kind = 2 AND u.IsEmployee = 0 AND u.IsSystem = 0)
            OR (@Kind = 3 AND u.IsSystem = 1))
      AND (@Filter IS NULL
            OR (@Filter = 1 AND u.IsActive = 0)
            OR (@Filter = 2 AND u.IsActive = 1 AND u.IsEmployee = 0 AND u.IsSystem = 0 AND u.ValidUntil BETWEEN @Today AND @Expiring)
            OR (@Filter = 3 AND u.IsActive = 1 AND s.HasPassword = 1 AND s.TwoFactorEnabled = 0))
      AND (@CompanyId IS NULL OR EXISTS (
            SELECT 1 FROM core.fn_AssignmentScope(u.Id) AS a WHERE a.CompanyId = @CompanyId))
      AND (@RoleId IS NULL OR EXISTS (
            SELECT 1 FROM core.UserRoleAssignment AS a WHERE a.UserId = u.Id AND a.RoleId = @RoleId))
    ORDER BY u.NameSearch, u.Id
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
    OPTION (RECOMPILE);

    SELECT Id, SubjectId, UserName, DisplayName, Email, IsActive, IsSystem, IsEmployee, EmployeeNo, EmployeeLeaveDate, ExternalAccessType,
           OrganizationName, ValidUntil, TwoFactorEnabled, LastLoginAt, TotalCount
    FROM #Page
    ORDER BY SortKey, Id;

    SELECT a.UserId, a.CompanyId, c.Name AS CompanyName, r.Code AS RoleCode, r.Name AS RoleName, a.CompanyGroupId, g.Name AS GroupName,
           a.BranchId, b.Name AS BranchName, a.LocationId, l.Name AS LocationName
    FROM core.UserRoleAssignment AS a
    LEFT JOIN core.CompanyGroup AS g ON g.Id = a.CompanyGroupId
    LEFT JOIN core.Branch AS b ON b.Id = a.BranchId
    LEFT JOIN core.Location AS l ON l.Id = a.LocationId
    JOIN #Page AS pg ON pg.Id = a.UserId
    JOIN core.Role AS r ON r.Id = a.RoleId
    LEFT JOIN core.Company AS c ON c.Id = a.CompanyId
    ORDER BY a.UserId, CASE WHEN a.CompanyId IS NULL AND a.CompanyGroupId IS NULL THEN 0 WHEN a.CompanyGroupId IS NOT NULL THEN 1 ELSE 2 END,
        g.Name, c.Name, b.Name, l.Name, r.Name;

    -- Indicatorii din antet. Utilizatorii activi sunt cei care consuma licenta: fara conturile de sistem.
    SELECT SUM(CASE WHEN u.IsActive = 1 AND u.IsSystem = 0 THEN 1 ELSE 0 END) AS ActiveCount,
           SUM(CASE WHEN u.IsActive = 1 AND u.IsEmployee = 1 THEN 1 ELSE 0 END) AS EmployeeCount,
           SUM(CASE WHEN u.IsActive = 1 AND u.IsEmployee = 0 AND u.IsSystem = 0 THEN 1 ELSE 0 END) AS ExternalCount,
           SUM(CASE WHEN u.IsActive = 1 AND u.IsEmployee = 0 AND u.IsSystem = 0 AND u.ValidUntil BETWEEN @Today AND @Expiring
                    THEN 1 ELSE 0 END) AS ExpiringCount,
           SUM(CASE WHEN u.IsActive = 1 AND s.HasPassword = 1 AND s.TwoFactorEnabled = 0 THEN 1 ELSE 0 END) AS WithoutTwoFactorCount,
           SUM(CASE WHEN u.IsActive = 0 THEN 1 ELSE 0 END) AS InactiveCount
    FROM core.[User] AS u
    LEFT JOIN ids.v_UserAccountSecurity AS s ON s.SubjectId = u.SubjectId
    WHERE u.Id <> 1;
END;
