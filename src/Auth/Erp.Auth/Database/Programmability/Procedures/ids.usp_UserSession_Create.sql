CREATE OR ALTER PROCEDURE ids.usp_UserSession_Create
    @SessionId     uniqueidentifier,
    @UserAccountId int,
    @IpAddress     varchar(45),
    @UserAgent     nvarchar(300)
AS
BEGIN
    -- O sesiune noua la autentificare (08, 8.7); SessionId ajunge in cookie ca claim-ul sid.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO ids.UserSession (SessionId, UserAccountId, IpAddress, UserAgent)
    VALUES (@SessionId, @UserAccountId, @IpAddress, @UserAgent);
END;
