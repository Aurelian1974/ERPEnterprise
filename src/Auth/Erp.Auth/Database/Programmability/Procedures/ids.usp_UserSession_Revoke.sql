CREATE OR ALTER PROCEDURE ids.usp_UserSession_Revoke
    @SessionId     uniqueidentifier = NULL,
    @UserAccountId int = NULL
AS
BEGIN
    -- Inchide o sesiune (deconectare, „Inchide” din Profilul meu) sau toate sesiunile unui cont
    -- (schimbarea parolei, dezactivarea contului, 02.01 3.4 si 11.2). Cu ambii parametri, sesiunea se inchide doar daca e a contului
    -- (administratorul inchide o sesiune din fisa utilizatorului).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE ids.UserSession
    SET RevokedAt = SYSUTCDATETIME()
    WHERE RevokedAt IS NULL
      AND ((@SessionId IS NOT NULL AND SessionId = @SessionId AND (@UserAccountId IS NULL OR UserAccountId = @UserAccountId))
        OR (@SessionId IS NULL AND @UserAccountId IS NOT NULL AND UserAccountId = @UserAccountId));
END;
