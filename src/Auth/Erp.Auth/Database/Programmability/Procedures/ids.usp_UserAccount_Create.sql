CREATE OR ALTER PROCEDURE ids.usp_UserAccount_Create
    @SubjectId          uniqueidentifier,
    @UserName           nvarchar(50),
    @Email              nvarchar(256) = NULL,
    @PasswordHash       nvarchar(500) = NULL,
    @TwoFactorRequired  bit,
    @SecurityStamp      nvarchar(64),
    @ConcurrencyStamp   nvarchar(64),
    @CreatedBy          int
AS
BEGIN
    -- Contul unui utilizator nou din administrarea utilizatorilor (02.01, 3.4 si 11), in tranzactia comenzii (SP-03):
    -- parola temporara cu schimbare obligatorie, sau fara parola pentru conturile tehnice (nu se pot autentifica interactiv).
    -- Emailul nu se considera confirmat.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @UserName <> LOWER(@UserName)
        THROW 64001, N'Numele de utilizator trebuie scris cu litere mici.', 1;
    IF EXISTS (SELECT 1 FROM ids.UserAccount WHERE NormalizedUserName = UPPER(@UserName))
        THROW 64002, N'Există deja un cont cu acest nume de utilizator.', 1;

    INSERT INTO ids.UserAccount
        (SubjectId, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
         PasswordHash, PasswordChangedAt, MustChangePassword, SecurityStamp, ConcurrencyStamp,
         TwoFactorEnabled, TwoFactorRequired, LockoutEnabled, AccessFailedCount, IsActive, CreatedBy)
    VALUES
        (@SubjectId, @UserName, UPPER(@UserName), @Email, UPPER(@Email), 0,
         @PasswordHash, CASE WHEN @PasswordHash IS NULL THEN NULL ELSE SYSUTCDATETIME() END,
         CASE WHEN @PasswordHash IS NULL THEN 0 ELSE 1 END, @SecurityStamp, @ConcurrencyStamp,
         0, @TwoFactorRequired, 1, 0, 1, @CreatedBy);

    SELECT CAST(SCOPE_IDENTITY() AS int) AS Id;
END;
