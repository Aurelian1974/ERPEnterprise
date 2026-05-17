CREATE OR ALTER PROCEDURE administration.usp_UpsertPartnerBankAccount
    @Id        BIGINT           = NULL,
    @PartnerId UNIQUEIDENTIFIER,
    @TenantId  UNIQUEIDENTIFIER,
    @Iban      NVARCHAR(34),
    @BankName  NVARCHAR(200),
    @Currency  NVARCHAR(3)      = N'RON',
    @IsDefault BIT              = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF @Id IS NULL
    BEGIN
        INSERT INTO administration.partner_bank_accounts
            (partner_id, tenant_id, iban, bank_name, currency, is_default)
        VALUES
            (@PartnerId, @TenantId, @Iban, @BankName, @Currency, @IsDefault);
    END
    ELSE
    BEGIN
        UPDATE administration.partner_bank_accounts
        SET
            iban       = @Iban,
            bank_name  = @BankName,
            currency   = @Currency,
            is_default = @IsDefault
        WHERE id         = @Id
            AND partner_id = @PartnerId
            AND tenant_id  = @TenantId;
    END;
END;
GO
