CREATE OR ALTER PROCEDURE core.usp_IdempotencyKey_Complete
    @UserId     int,
    @Key        nvarchar(100),
    @ResultJson nvarchar(max)
AS
BEGIN
    -- 4.1: rezultatul primei cereri, in aceeasi tranzactie cu documentul; retrimiterea il primeste neschimbat.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE core.IdempotencyKey
    SET ResultJson = @ResultJson
    WHERE UserId = @UserId AND [Key] = @Key;
END;
