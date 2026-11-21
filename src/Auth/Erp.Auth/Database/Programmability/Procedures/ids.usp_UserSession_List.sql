CREATE OR ALTER PROCEDURE ids.usp_UserSession_List
    @SubjectId uniqueidentifier
AS
BEGIN
    -- Sesiunile active ale unui cont (02.01, 11: tab-ul Sesiuni din fisa utilizatorului), cele mai recente intai.
    SET NOCOUNT ON;

    SELECT s.SessionId, s.CreatedAt, s.LastSeenAt, s.IpAddress, s.UserAgent
    FROM ids.UserSession AS s
    JOIN ids.UserAccount AS ua ON ua.Id = s.UserAccountId
    WHERE ua.SubjectId = @SubjectId
      AND s.RevokedAt IS NULL
    ORDER BY s.LastSeenAt DESC, s.Id DESC;
END;
