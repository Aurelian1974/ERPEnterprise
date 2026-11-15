CREATE OR ALTER PROCEDURE core.usp_LoginEvent_Insert
    @UserName      nvarchar(256),
    @SubjectId     uniqueidentifier,
    @Success       bit,
    @FailureReason varchar(50),
    @IpAddress     varchar(45),
    @UserAgent     nvarchar(300),
    @LockedOut     bit,
    @CorrelationId uniqueidentifier
AS
BEGIN
    -- Jurnalul de autentificari (02.01, 9): fiecare incercare. La succes se actualizeaza core.[User].LastLoginAt.
    -- Cand incercarea curenta blocheaza contul (A20), evenimentul UtilizatorBlocat intra in outbox in aceeasi tranzactie;
    -- notificarea administratorilor il consuma (4.7).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OwnTransaction bit = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;
    IF @OwnTransaction = 1 BEGIN TRANSACTION;

    DECLARE @Now datetime2(3) = SYSUTCDATETIME();
    DECLARE @UserId int = (SELECT Id FROM core.[User] WHERE SubjectId = @SubjectId);

    INSERT INTO core.LoginEvent (OccurredAt, UserName, UserId, Success, FailureReason, IpAddress, UserAgent)
    VALUES (@Now, @UserName, @UserId, @Success, @FailureReason, @IpAddress, @UserAgent);

    IF @Success = 1 AND @UserId IS NOT NULL
        UPDATE core.[User] SET LastLoginAt = @Now WHERE Id = @UserId;

    IF @LockedOut = 1
        INSERT INTO core.Outbox (EventId, EventType, EventVersion, CompanyId, CorrelationId, Payload, OccurredAt)
        VALUES (NEWID(), N'Erp.Auth.UtilizatorBlocat', 1, NULL, @CorrelationId,
                (SELECT @UserId AS userId, @UserName AS userName, @IpAddress AS ipAddress, @Now AS occurredAtUtc
                 FOR JSON PATH, WITHOUT_ARRAY_WRAPPER),
                @Now);

    IF @OwnTransaction = 1 COMMIT TRANSACTION;
END;
