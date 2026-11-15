CREATE OR ALTER PROCEDURE ids.usp_UserRecoveryCode_Redeem
    @UserAccountId int,
    @CodeHash      char(64)
AS
BEGIN
    -- Un cod de rezerva se foloseste o singura data; intoarce 1 daca a fost consumat acum, 0 altfel.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE ids.UserRecoveryCode
    SET UsedAt = SYSUTCDATETIME()
    WHERE UserAccountId = @UserAccountId AND CodeHash = @CodeHash AND UsedAt IS NULL;

    SELECT CAST(@@ROWCOUNT AS int) AS Redeemed;
END;
