CREATE OR ALTER PROCEDURE ids.usp_UserAccount_Update
    @Id                        int,
    @ExpectedConcurrencyStamp  nvarchar(64),
    @ConcurrencyStamp          nvarchar(64),
    @Email                     nvarchar(256),
    @NormalizedEmail           nvarchar(256),
    @EmailConfirmed            bit,
    @PasswordHash              nvarchar(500),
    @PasswordChangedAt         datetime2(3),
    @MustChangePassword        bit,
    @SecurityStamp             nvarchar(64),
    @TwoFactorEnabled          bit,
    @AuthenticatorKeyEncrypted varbinary(512),
    @LockoutEnabled            bit,
    @LockoutEndAt              datetime2(3),
    @AccessFailedCount         int,
    @ModifiedBy                int
AS
BEGIN
    -- Starea contului scrisa de store-ul ASP.NET Core Identity (08, 8.7). Concurenta optimista pe ConcurrencyStamp:
    -- o modificare facuta intre timp de alta cerere da 64003. Numele de utilizator nu se schimba aici.
    -- Parola noua intra si in ids.PasswordHistory (regula „diferita de ultimele 5”).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @OwnTransaction bit = CASE WHEN @@TRANCOUNT = 0 THEN 1 ELSE 0 END;
    IF @OwnTransaction = 1 BEGIN TRANSACTION;

    DECLARE @PreviousHash nvarchar(500);
    SELECT @PreviousHash = PasswordHash
    FROM ids.UserAccount WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @Id AND ConcurrencyStamp = @ExpectedConcurrencyStamp;

    IF @@ROWCOUNT = 0
        THROW 64003, N'Contul a fost modificat între timp; reîncercați.', 1;

    UPDATE ids.UserAccount
    SET ConcurrencyStamp = @ConcurrencyStamp,
        Email = @Email,
        NormalizedEmail = @NormalizedEmail,
        EmailConfirmed = @EmailConfirmed,
        PasswordHash = @PasswordHash,
        PasswordChangedAt = @PasswordChangedAt,
        MustChangePassword = @MustChangePassword,
        SecurityStamp = @SecurityStamp,
        TwoFactorEnabled = @TwoFactorEnabled,
        AuthenticatorKeyEncrypted = @AuthenticatorKeyEncrypted,
        LockoutEnabled = @LockoutEnabled,
        LockoutEndAt = @LockoutEndAt,
        AccessFailedCount = @AccessFailedCount,
        ModifiedAt = SYSUTCDATETIME(),
        ModifiedBy = @ModifiedBy
    WHERE Id = @Id;

    IF @PasswordHash IS NOT NULL AND (@PreviousHash IS NULL OR @PreviousHash <> @PasswordHash)
        INSERT INTO ids.PasswordHistory (UserAccountId, PasswordHash) VALUES (@Id, @PasswordHash);

    IF @OwnTransaction = 1 COMMIT TRANSACTION;
END;
