CREATE OR ALTER PROCEDURE ids.usp_UserAccount_AdminReset
    @SubjectId          uniqueidentifier,
    @Action             varchar(20),
    @PasswordHash       nvarchar(500) = NULL,
    @SecurityStamp      nvarchar(64),
    @ConcurrencyStamp   nvarchar(64),
    @ModifiedBy         int
AS
BEGIN
    -- Actiunile administratorului din tab-ul Securitate (02.01, 3.4, 8 si 11; Core.User.Manage), in tranzactia comenzii:
    --   ResetPassword  parola temporara noua, schimbare obligatorie, cont deblocat; sesiunile se inchid
    --   Unlock         ridica blocarea dupa incercari gresite
    --   ResetTwoFactor dezactiveaza aplicatia de autentificare si sterge codurile de rezerva; sesiunile se inchid
    --   RevokeSessions inchide toate sesiunile
    -- Fiecare actiune schimba SecurityStamp (cookie-urile emise inainte nu mai sunt acceptate), in afara de Unlock.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Id int;
    SELECT @Id = Id FROM ids.UserAccount WITH (UPDLOCK, ROWLOCK) WHERE SubjectId = @SubjectId;

    IF @Id IS NULL
        THROW 64004, N'Contul de autentificare nu există.', 1;
    IF @Action NOT IN ('ResetPassword', 'Unlock', 'ResetTwoFactor', 'RevokeSessions')
        THROW 64005, N'Acțiune necunoscută pe contul de autentificare.', 1;
    IF @Action = 'ResetPassword' AND @PasswordHash IS NULL
        THROW 64005, N'Parola temporară lipsește.', 1;

    UPDATE ids.UserAccount
    SET PasswordHash = CASE WHEN @Action = 'ResetPassword' THEN @PasswordHash ELSE PasswordHash END,
        PasswordChangedAt = CASE WHEN @Action = 'ResetPassword' THEN SYSUTCDATETIME() ELSE PasswordChangedAt END,
        MustChangePassword = CASE WHEN @Action = 'ResetPassword' THEN 1 ELSE MustChangePassword END,
        TwoFactorEnabled = CASE WHEN @Action = 'ResetTwoFactor' THEN 0 ELSE TwoFactorEnabled END,
        AuthenticatorKeyEncrypted = CASE WHEN @Action = 'ResetTwoFactor' THEN NULL ELSE AuthenticatorKeyEncrypted END,
        AccessFailedCount = CASE WHEN @Action IN ('ResetPassword', 'Unlock') THEN 0 ELSE AccessFailedCount END,
        LockoutEndAt = CASE WHEN @Action IN ('ResetPassword', 'Unlock') THEN NULL ELSE LockoutEndAt END,
        SecurityStamp = CASE WHEN @Action = 'Unlock' THEN SecurityStamp ELSE @SecurityStamp END,
        ConcurrencyStamp = @ConcurrencyStamp,
        ModifiedAt = SYSUTCDATETIME(),
        ModifiedBy = @ModifiedBy
    WHERE Id = @Id;

    IF @Action = 'ResetPassword'
        INSERT INTO ids.PasswordHistory (UserAccountId, PasswordHash) VALUES (@Id, @PasswordHash);

    IF @Action = 'ResetTwoFactor'
        DELETE FROM ids.UserRecoveryCode WHERE UserAccountId = @Id;

    IF @Action IN ('ResetPassword', 'ResetTwoFactor', 'RevokeSessions')
        EXEC ids.usp_UserSession_Revoke @UserAccountId = @Id;
END;
