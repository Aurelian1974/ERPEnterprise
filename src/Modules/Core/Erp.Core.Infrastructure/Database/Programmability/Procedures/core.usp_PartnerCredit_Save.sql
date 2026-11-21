CREATE OR ALTER PROCEDURE core.usp_PartnerCredit_Save
    @PartnerId       int,
    @CompanyId       int,
    @CreditLimit     decimal(19,2) = NULL,
    @OverLimitAction tinyint,
    @RowVersion      binary(8) = NULL,
    @UserId          int
AS
BEGIN
    -- Limita de credit a clientului pe firma (02.01, 3.6 punctul 6): NULL = fara limita; la depasire avertizare (1) sau blocare (2).
    -- Doar pentru clienti (50021). Concurenta optimista pe RowVersion (50006).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @PartnerId AND IsCustomer = 1)
        THROW 50021, N'Limita de credit se stabilește doar pentru clienți.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId)
        THROW 50021, N'Firma nu există.', 1;

    IF EXISTS (SELECT 1 FROM core.PartnerCredit WITH (UPDLOCK, HOLDLOCK) WHERE PartnerId = @PartnerId AND CompanyId = @CompanyId)
    BEGIN
        UPDATE core.PartnerCredit
        SET CreditLimit = @CreditLimit, OverLimitAction = @OverLimitAction, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE PartnerId = @PartnerId AND CompanyId = @CompanyId AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 50006, N'Limita de credit a fost modificată de alt utilizator. Reîncărcați fișa.', 1;
    END
    ELSE
        INSERT INTO core.PartnerCredit (PartnerId, CompanyId, CreditLimit, OverLimitAction, ModifiedAt, ModifiedBy)
        VALUES (@PartnerId, @CompanyId, @CreditLimit, @OverLimitAction, SYSUTCDATETIME(), @UserId);

    SELECT @PartnerId AS Id, RowVersion FROM core.PartnerCredit WHERE PartnerId = @PartnerId AND CompanyId = @CompanyId;
END;
