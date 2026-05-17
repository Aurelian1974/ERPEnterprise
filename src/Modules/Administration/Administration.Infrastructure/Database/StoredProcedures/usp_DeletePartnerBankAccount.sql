CREATE OR ALTER PROCEDURE administration.usp_DeletePartnerBankAccount
    @Id        BIGINT,
    @PartnerId UNIQUEIDENTIFIER,
    @TenantId  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM administration.partner_bank_accounts
    WHERE id         = @Id
        AND partner_id = @PartnerId
        AND tenant_id  = @TenantId;
END;
GO
