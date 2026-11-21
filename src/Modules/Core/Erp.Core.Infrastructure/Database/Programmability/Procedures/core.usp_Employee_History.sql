CREATE OR ALTER PROCEDURE core.usp_Employee_History
    @Id  int,
    @Top int = 200
AS
BEGIN
    -- Tab-ul Istoric al fisei angajatului (02.01, 11), cele mai noi intai.
    SET NOCOUNT ON;

    SELECT TOP (@Top) a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, u.DisplayName AS UserName, a.Changes
    FROM core.AuditEntry AS a
    JOIN core.[User] AS u ON u.Id = a.UserId
    WHERE a.EntityType = N'Core.Employee' AND a.EntityId = @Id
    ORDER BY a.Id DESC;
END;
