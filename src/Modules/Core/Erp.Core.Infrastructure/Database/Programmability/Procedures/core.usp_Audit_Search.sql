CREATE OR ALTER PROCEDURE core.usp_Audit_Search
    @UserId      int,
    @From        datetime2(3) = NULL,
    @To          datetime2(3) = NULL,
    @ActorUserId int = NULL,
    @EntityType  nvarchar(100) = NULL,
    @EntityId    bigint = NULL,
    @Action      nvarchar(100) = NULL,
    @Search      nvarchar(100) = NULL,
    @Offset      int = 0,
    @PageSize    int = 50
AS
BEGIN
    -- Configurare › Audit (02.01, 11; macheta 07h), doar citire. Se vad intrarile firmelor pe care utilizatorul are Core.Audit.View;
    -- intrarile fara firma (utilizatori, roluri, licenta, nomenclatoare comune) doar cu dreptul pe toate firmele.
    -- @From inclusiv, @To exclusiv (UTC). @Search e deja protejat pentru LIKE; cauta in Changes, in numele utilizatorului si Id-ul exact.
    -- Intoarce, in ordine: AllCompanies; pagina (cu TotalCount; IsFirst = prima intrare a inregistrarii, adica crearea); utilizatorii, entitatile si actiunile din interval (pentru filtre).
    SET NOCOUNT ON;

    CREATE TABLE #Companies (Id int NOT NULL PRIMARY KEY);
    INSERT INTO #Companies (Id) SELECT f.CompanyId FROM core.fn_UserCompanies(@UserId, N'Core.Audit.View') AS f;

    DECLARE @AllCompanies bit = CASE WHEN EXISTS (SELECT 1 FROM core.Company AS c WHERE NOT EXISTS (SELECT 1 FROM #Companies AS x WHERE x.Id = c.Id))
                                     THEN 0 ELSE 1 END;
    DECLARE @SearchId bigint = TRY_CAST(@Search AS bigint);

    SELECT @AllCompanies AS AllCompanies;

    WITH Filtered AS (
        SELECT a.Id, COUNT(*) OVER () AS TotalCount
        FROM core.AuditEntry AS a
        JOIN core.[User] AS u ON u.Id = a.UserId
        WHERE (@From IS NULL OR a.OccurredAt >= @From)
          AND (@To IS NULL OR a.OccurredAt < @To)
          AND (a.CompanyId IN (SELECT x.Id FROM #Companies AS x) OR (a.CompanyId IS NULL AND @AllCompanies = 1))
          AND (@ActorUserId IS NULL OR a.UserId = @ActorUserId)
          AND (@EntityType IS NULL OR a.EntityType = @EntityType)
          AND (@EntityId IS NULL OR a.EntityId = @EntityId)
          AND (@Action IS NULL OR a.Action = @Action)
          AND (@Search IS NULL OR a.Changes LIKE N'%' + @Search + N'%' OR u.DisplayName LIKE N'%' + @Search + N'%'
               OR u.UserName LIKE @Search + N'%' OR a.EntityId = @SearchId)
        ORDER BY a.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
    )
    SELECT a.Id, a.OccurredAt, a.UserId, u.DisplayName AS UserName, a.Action, a.EntityType, a.EntityId, l.RecordLabel, c.Name AS CompanyName,
           CAST(CASE WHEN a.EntityId IS NOT NULL AND NOT EXISTS (
                         SELECT 1 FROM core.AuditEntry AS p WHERE p.EntityType = a.EntityType AND p.EntityId = a.EntityId AND p.Id < a.Id)
                     THEN 1 ELSE 0 END AS bit) AS IsFirst,
           f.TotalCount
    FROM Filtered AS f
    JOIN core.AuditEntry AS a ON a.Id = f.Id
    JOIN core.[User] AS u ON u.Id = a.UserId
    LEFT JOIN core.Company AS c ON c.Id = a.CompanyId
    OUTER APPLY core.fn_AuditRecordLabel(a.EntityType, a.EntityId, a.Changes) AS l
    ORDER BY a.Id DESC;

    -- Valorile filtrelor, din intervalul vizibil (indexul IX_AuditEntry_OccurredAt acopera coloanele).
    CREATE TABLE #Facets (UserId int NOT NULL, EntityType nvarchar(100) COLLATE DATABASE_DEFAULT NOT NULL,
                          Action nvarchar(100) COLLATE DATABASE_DEFAULT NOT NULL);
    INSERT INTO #Facets (UserId, EntityType, Action)
    SELECT a.UserId, a.EntityType, a.Action
    FROM core.AuditEntry AS a
    WHERE (@From IS NULL OR a.OccurredAt >= @From)
      AND (@To IS NULL OR a.OccurredAt < @To)
      AND (a.CompanyId IN (SELECT x.Id FROM #Companies AS x) OR (a.CompanyId IS NULL AND @AllCompanies = 1))
    GROUP BY a.UserId, a.EntityType, a.Action;

    SELECT u.Id, u.DisplayName
    FROM core.[User] AS u
    WHERE u.Id IN (SELECT f.UserId FROM #Facets AS f)
    ORDER BY u.DisplayName, u.Id;

    SELECT DISTINCT f.EntityType FROM #Facets AS f ORDER BY f.EntityType;

    SELECT DISTINCT f.Action FROM #Facets AS f ORDER BY f.Action;
END;
