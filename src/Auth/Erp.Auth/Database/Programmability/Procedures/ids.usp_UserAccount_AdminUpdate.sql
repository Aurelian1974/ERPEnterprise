CREATE OR ALTER PROCEDURE ids.usp_UserAccount_AdminUpdate
    @SubjectId          uniqueidentifier,
    @UserName           nvarchar(50),
    @Email              nvarchar(256) = NULL,
    @IsActive           bit,
    @TwoFactorRequired  bit,
    @NewSecurityStamp   nvarchar(64) = NULL,
    @ConcurrencyStamp   nvarchar(64),
    @ModifiedBy         int
AS
BEGIN
    -- Datele contului schimbate din fisa utilizatorului (02.01, 3.4): nume, email, stare, 2FA obligatoriu. Un SecurityStamp nou
    -- (dezactivare, nume schimbat) respinge cookie-urile existente in cel mult un minut; dezactivarea inchide si sesiunile.
    -- Tranzactia e a comenzii (SP-03).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Id int;
    SELECT @Id = Id FROM ids.UserAccount WITH (UPDLOCK, ROWLOCK) WHERE SubjectId = @SubjectId;

    IF @Id IS NULL
        THROW 64004, N'Contul de autentificare nu există.', 1;
    IF @UserName <> LOWER(@UserName)
        THROW 64001, N'Numele de utilizator trebuie scris cu litere mici.', 1;
    IF EXISTS (SELECT 1 FROM ids.UserAccount WHERE NormalizedUserName = UPPER(@UserName) AND Id <> @Id)
        THROW 64002, N'Există deja un cont cu acest nume de utilizator.', 1;

    UPDATE ids.UserAccount
    SET UserName = @UserName,
        NormalizedUserName = UPPER(@UserName),
        EmailConfirmed = CASE WHEN ISNULL(Email, N'') = ISNULL(@Email, N'') THEN EmailConfirmed ELSE 0 END,
        Email = @Email,
        NormalizedEmail = UPPER(@Email),
        IsActive = @IsActive,
        TwoFactorRequired = @TwoFactorRequired,
        SecurityStamp = COALESCE(@NewSecurityStamp, SecurityStamp),
        ConcurrencyStamp = @ConcurrencyStamp,
        ModifiedAt = SYSUTCDATETIME(),
        ModifiedBy = @ModifiedBy
    WHERE Id = @Id;

    IF @IsActive = 0
        EXEC ids.usp_UserSession_Revoke @UserAccountId = @Id;
END;
