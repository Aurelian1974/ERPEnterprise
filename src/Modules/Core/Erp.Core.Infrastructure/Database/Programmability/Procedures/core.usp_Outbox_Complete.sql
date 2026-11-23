CREATE OR ALTER PROCEDURE core.usp_Outbox_Complete
    @Id  bigint,
    @Now datetime2(3)
AS
BEGIN
    -- Mesajul a fost livrat tuturor consumatorilor (02.01, 4.5).
    SET NOCOUNT ON;
    UPDATE core.Outbox SET Status = 1, ProcessedAt = @Now, NextAttemptAt = NULL, LastError = NULL WHERE Id = @Id;
END;
