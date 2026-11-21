CREATE OR ALTER PROCEDURE core.usp_User_History
    @Id  int,
    @Top int = 200
AS
BEGIN
    -- Tab-ul Istoric al fisei utilizatorului (02.01, 11): modificarile contului, ale rolurilor si actiunile de securitate
    -- (resetare parola, deblocare, resetare 2FA, sesiuni inchise), cele mai noi intai. Fara parole in Changes (NET-18).
    SET NOCOUNT ON;

    SELECT TOP (@Top) a.Id, a.OccurredAt, a.Action, a.EntityType, a.EntityId, u.DisplayName AS UserName, a.Changes
    FROM core.AuditEntry AS a
    JOIN core.[User] AS u ON u.Id = a.UserId
    WHERE a.EntityType = N'Core.User' AND a.EntityId = @Id
    ORDER BY a.Id DESC;
END;
