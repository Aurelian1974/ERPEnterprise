CREATE OR ALTER PROCEDURE core.usp_LoginEvent_Search
    @UserId   int,
    @From     datetime2(3) = NULL,
    @To       datetime2(3) = NULL,
    @Search   nvarchar(100) = NULL,
    @Success  bit = NULL,
    @Offset   int = 0,
    @PageSize int = 50
AS
BEGIN
    -- Jurnalul de autentificari (02.01, 9; macheta 07h, „Autentificări”), doar citire. Incercarile nu tin de o firma, deci se vad
    -- doar cu Core.Audit.View pe toate firmele. Intoarce, in ordine: AllCompanies; pagina (cu TotalCount), goala fara drept.
    SET NOCOUNT ON;

    DECLARE @AllCompanies bit = CASE WHEN EXISTS (
            SELECT 1 FROM core.Company AS c
            WHERE NOT EXISTS (SELECT 1 FROM core.fn_UserCompanies(@UserId, N'Core.Audit.View') AS f WHERE f.CompanyId = c.Id))
        THEN 0 ELSE 1 END;

    SELECT @AllCompanies AS AllCompanies;

    SELECT e.Id, e.OccurredAt, e.UserName, e.UserId, u.DisplayName, e.Success, e.FailureReason, e.IpAddress, e.UserAgent,
           COUNT(*) OVER () AS TotalCount
    FROM core.LoginEvent AS e
    LEFT JOIN core.[User] AS u ON u.Id = e.UserId
    WHERE @AllCompanies = 1
      AND (@From IS NULL OR e.OccurredAt >= @From)
      AND (@To IS NULL OR e.OccurredAt < @To)
      AND (@Success IS NULL OR e.Success = @Success)
      AND (@Search IS NULL OR e.UserName LIKE @Search + N'%' OR u.DisplayName LIKE N'%' + @Search + N'%' OR e.IpAddress LIKE @Search + N'%')
    ORDER BY e.Id DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
