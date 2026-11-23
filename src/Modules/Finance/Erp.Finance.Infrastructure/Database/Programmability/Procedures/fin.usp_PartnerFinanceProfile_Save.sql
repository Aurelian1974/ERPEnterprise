CREATE OR ALTER PROCEDURE fin.usp_PartnerFinanceProfile_Save
    @CompanyId                int,
    @PartnerId                int,
    @Role                     tinyint,
    @AutoAllocationMode       tinyint,
    @PenaltyRatePerDay        decimal(9,6),
    @PenaltyGraceDays         int,
    @PenaltyCapAtPrincipal    bit,
    @ApplyFixedRecoveryCost   bit,
    @IsPaymentBlocked         bit,
    @BlockReason              nvarchar(500) = NULL,
    @BalanceConfirmationEmail nvarchar(200) = NULL,
    @RowVersion               binary(8) = NULL,
    @UserId                   int
AS
BEGIN
    -- Salveaza profilul financiar (F3 3.2): randul se creeaza daca lipseste; existent cu alta versiune: 51417. Partenerul exista (51021).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId)
        THROW 51021, N'Partenerul nu există.', 1;

    DECLARE @Version binary(8), @Exists bit = 0;
    SELECT @Version = RowVersion, @Exists = 1 FROM fin.PartnerFinanceProfile WITH (UPDLOCK, HOLDLOCK)
    WHERE CompanyId = @CompanyId AND PartnerId = @PartnerId AND Role = @Role;

    IF @Exists = 1 AND (@RowVersion IS NULL OR @Version <> @RowVersion)
        THROW 51417, N'Profilul a fost modificat de alt utilizator; reîncărcați datele.', 1;

    IF @Exists = 0
        INSERT INTO fin.PartnerFinanceProfile (CompanyId, PartnerId, Role, AutoAllocationMode, PenaltyRatePerDay, PenaltyGraceDays, PenaltyCapAtPrincipal,
            ApplyFixedRecoveryCost, IsPaymentBlocked, BlockReason, BalanceConfirmationEmail, CreatedBy)
        OUTPUT inserted.RowVersion
        VALUES (@CompanyId, @PartnerId, @Role, @AutoAllocationMode, @PenaltyRatePerDay, @PenaltyGraceDays, @PenaltyCapAtPrincipal,
            @ApplyFixedRecoveryCost, @IsPaymentBlocked, CASE WHEN @IsPaymentBlocked = 1 THEN @BlockReason END, @BalanceConfirmationEmail, @UserId);
    ELSE
        UPDATE fin.PartnerFinanceProfile
        SET AutoAllocationMode = @AutoAllocationMode, PenaltyRatePerDay = @PenaltyRatePerDay, PenaltyGraceDays = @PenaltyGraceDays,
            PenaltyCapAtPrincipal = @PenaltyCapAtPrincipal, ApplyFixedRecoveryCost = @ApplyFixedRecoveryCost, IsPaymentBlocked = @IsPaymentBlocked,
            BlockReason = CASE WHEN @IsPaymentBlocked = 1 THEN @BlockReason END, BalanceConfirmationEmail = @BalanceConfirmationEmail,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        OUTPUT inserted.RowVersion
        WHERE CompanyId = @CompanyId AND PartnerId = @PartnerId AND Role = @Role;
END;
