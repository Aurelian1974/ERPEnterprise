CREATE OR ALTER PROCEDURE ids.usp_UserSession_Touch
    @SessionId uniqueidentifier
AS
BEGIN
    -- Verificarea periodica a cookie-ului (cel mult o data pe minut, 8.7): sesiunea exista si nu e revocata.
    -- Intoarce 1 daca sesiunea e valida (si actualizeaza LastSeenAt), 0 altfel.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE ids.UserSession
    SET LastSeenAt = SYSUTCDATETIME()
    WHERE SessionId = @SessionId AND RevokedAt IS NULL;

    SELECT CAST(@@ROWCOUNT AS int) AS IsValid;
END;
