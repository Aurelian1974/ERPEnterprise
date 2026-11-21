CREATE OR ALTER PROCEDURE core.usp_User_DeactivateExpired
    @Today         date,
    @CorrelationId uniqueidentifier
AS
BEGIN
    -- Jobul zilnic (02.01, 3.4): dezactiveaza externii al caror acces a expirat (ValidUntil < azi) si angajatii cu contractul
    -- incetat (LeaveDate <= azi). Autentificarea ii refuza deja (core.v_UserLoginStatus); jobul trece contul pe inactiv, scrie
    -- auditul ca utilizatorul „system” si evenimentul UtilizatorDezactivat in outbox, in aceeasi tranzactie (P8).
    -- Intoarce conturile dezactivate, ca jobul sa le inchida sesiunile (ids). Conturile de sistem nu expira.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Now datetime2(3) = SYSUTCDATETIME();
    DECLARE @Done TABLE (Id int NOT NULL PRIMARY KEY, SubjectId uniqueidentifier NOT NULL, Reason varchar(20) NOT NULL,
        EventId uniqueidentifier NOT NULL DEFAULT (NEWID()));

    UPDATE u
    SET IsActive = 0, ModifiedAt = @Now, ModifiedBy = 1
    OUTPUT inserted.Id, inserted.SubjectId,
           CASE WHEN inserted.IsEmployee = 1 THEN 'EmployeeLeft' ELSE 'AccessExpired' END
    INTO @Done (Id, SubjectId, Reason)
    FROM core.[User] AS u
    LEFT JOIN core.Employee AS e ON e.Id = u.EmployeeId
    WHERE u.IsActive = 1 AND u.IsSystem = 0
      AND ((u.IsEmployee = 0 AND u.ValidUntil < @Today)
        OR (u.IsEmployee = 1 AND e.LeaveDate <= @Today));

    INSERT INTO core.AuditEntry (OccurredAt, UserId, CompanyId, Action, EntityType, EntityId, Changes, IpAddress, CorrelationId)
    SELECT @Now, 1, NULL, N'Core.User.DeactivateExpired', N'Core.User', d.Id,
           (SELECT CAST(0 AS bit) AS isActive, d.Reason AS reason FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           NULL, @CorrelationId
    FROM @Done AS d;

    INSERT INTO core.Outbox (EventId, EventType, EventVersion, CompanyId, CorrelationId, Payload, OccurredAt)
    SELECT d.EventId, N'Erp.Core.Contracts.UtilizatorDezactivat', 1, NULL, @CorrelationId,
           (SELECT d.Id AS userId, d.SubjectId AS subjectId, d.EventId AS eventId, @Now AS occurredAtUtc, 0 AS companyId,
                   @CorrelationId AS correlationId, 1 AS version
            FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
           @Now
    FROM @Done AS d;

    SELECT Id, SubjectId, Reason FROM @Done ORDER BY Id;
END;
