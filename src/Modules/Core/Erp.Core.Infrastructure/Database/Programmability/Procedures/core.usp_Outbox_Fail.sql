CREATE OR ALTER PROCEDURE core.usp_Outbox_Fail
    @Id            bigint,
    @Error         nvarchar(2000),
    @NextAttemptAt datetime2(3) = NULL,
    @Failed        bit
AS
BEGIN
    -- Livrarea a esuat (02.01, 4.5): o noua incercare la @NextAttemptAt (1 min, 5 min, 30 min, 2 h) sau, dupa ultima, starea Failed (2),
    -- vizibila in monitorul de integrari.
    SET NOCOUNT ON;
    UPDATE core.Outbox
    SET Attempts = Attempts + 1, LastError = @Error, NextAttemptAt = CASE WHEN @Failed = 1 THEN NULL ELSE @NextAttemptAt END,
        Status = CASE WHEN @Failed = 1 THEN 2 ELSE 0 END
    WHERE Id = @Id;
END;
