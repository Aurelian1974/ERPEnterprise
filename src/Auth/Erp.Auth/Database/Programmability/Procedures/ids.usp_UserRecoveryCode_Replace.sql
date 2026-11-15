CREATE OR ALTER PROCEDURE ids.usp_UserRecoveryCode_Replace
    @UserAccountId int,
    @Codes         ids.tt_RecoveryCode READONLY
AS
BEGIN
    -- Codurile de rezerva noi inlocuiesc complet setul vechi (02.01, 11.2): se afiseaza o singura data, se pastreaza doar hash-ul.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OwnTransaction bit = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;
    IF @OwnTransaction = 1 BEGIN TRANSACTION;

    DELETE FROM ids.UserRecoveryCode WHERE UserAccountId = @UserAccountId;

    INSERT INTO ids.UserRecoveryCode (UserAccountId, CodeHash)
    SELECT @UserAccountId, CodeHash FROM @Codes;

    IF @OwnTransaction = 1 COMMIT TRANSACTION;
END;
