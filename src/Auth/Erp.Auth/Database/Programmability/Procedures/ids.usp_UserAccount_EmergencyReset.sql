CREATE OR ALTER PROCEDURE ids.usp_UserAccount_EmergencyReset
    @UserName          nvarchar(50),
    @PasswordHash      nvarchar(500),
    @SecurityStamp     nvarchar(64),
    @ConcurrencyStamp  nvarchar(64)
AS
BEGIN
    -- „Resetează accesul administratorului” (INSTALARE_ACTUALIZARE.md 11.3): parola temporara noua, schimbare obligatorie,
    -- 2FA dezactivat si cerut din nou, contul deblocat si activ, codurile de recuperare si toate sesiunile anulate.
    -- Tranzactia e deschisa de installer (SP-03).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Id int, @SubjectId uniqueidentifier;
    SELECT @Id = Id, @SubjectId = SubjectId
    FROM ids.UserAccount
    WHERE NormalizedUserName = UPPER(@UserName);

    IF @Id IS NULL
        THROW 64003, N'Contul administratorului nu există.', 1;

    UPDATE ids.UserAccount
    SET PasswordHash = @PasswordHash,
        PasswordChangedAt = SYSUTCDATETIME(),
        MustChangePassword = 1,
        TwoFactorEnabled = 0,
        TwoFactorRequired = 1,
        AuthenticatorKeyEncrypted = NULL,
        AccessFailedCount = 0,
        LockoutEndAt = NULL,
        IsActive = 1,
        SecurityStamp = @SecurityStamp,
        ConcurrencyStamp = @ConcurrencyStamp,
        ModifiedAt = SYSUTCDATETIME(),
        ModifiedBy = 1 /* system */
    WHERE Id = @Id;

    DELETE FROM ids.UserRecoveryCode WHERE UserAccountId = @Id;
    EXEC ids.usp_UserSession_Revoke @UserAccountId = @Id;

    SELECT @SubjectId AS SubjectId;
END;
