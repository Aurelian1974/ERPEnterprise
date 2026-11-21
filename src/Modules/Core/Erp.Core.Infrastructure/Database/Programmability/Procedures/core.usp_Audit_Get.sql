CREATE OR ALTER PROCEDURE core.usp_Audit_Get
    @UserId int,
    @Id     bigint
AS
BEGIN
    -- Detaliul unei intrari din Configurare › Audit (macheta 07h), cu aceleasi reguli de vizibilitate ca usp_Audit_Search.
    -- Changes pastreaza valorile de dupa modificare; PreviousChanges = intrarea anterioara a aceleiasi inregistrari (valorile
    -- de dinainte), din care interfata face tabelul Camp / Inainte / Dupa. Niciun rand = intrarea nu exista sau nu e vizibila.
    SET NOCOUNT ON;

    CREATE TABLE #Companies (Id int NOT NULL PRIMARY KEY);
    INSERT INTO #Companies (Id) SELECT f.CompanyId FROM core.fn_UserCompanies(@UserId, N'Core.Audit.View') AS f;

    DECLARE @AllCompanies bit = CASE WHEN EXISTS (SELECT 1 FROM core.Company AS c WHERE NOT EXISTS (SELECT 1 FROM #Companies AS x WHERE x.Id = c.Id))
                                     THEN 0 ELSE 1 END;

    SELECT a.Id, a.OccurredAt, a.UserId, u.DisplayName AS UserName, u.UserName AS Login, a.CompanyId, c.Name AS CompanyName, a.Action,
           a.EntityType, a.EntityId, l.RecordLabel, a.Changes, p.Changes AS PreviousChanges, p.OccurredAt AS PreviousAt, a.IpAddress,
           a.CorrelationId
    FROM core.AuditEntry AS a
    JOIN core.[User] AS u ON u.Id = a.UserId
    LEFT JOIN core.Company AS c ON c.Id = a.CompanyId
    OUTER APPLY core.fn_AuditRecordLabel(a.EntityType, a.EntityId, a.Changes) AS l
    OUTER APPLY (
        SELECT TOP (1) prev.Changes, prev.OccurredAt
        FROM core.AuditEntry AS prev
        WHERE a.EntityId IS NOT NULL AND prev.EntityType = a.EntityType AND prev.EntityId = a.EntityId AND prev.Id < a.Id
          AND prev.Changes IS NOT NULL
        ORDER BY prev.Id DESC) AS p
    WHERE a.Id = @Id
      AND (a.CompanyId IN (SELECT x.Id FROM #Companies AS x) OR (a.CompanyId IS NULL AND @AllCompanies = 1));
END;
