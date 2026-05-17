CREATE OR ALTER PROCEDURE administration.usp_DeletePartnerAddress
    @Id        BIGINT,
    @PartnerId UNIQUEIDENTIFIER,
    @TenantId  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM administration.partner_addresses
    WHERE id         = @Id
        AND partner_id = @PartnerId
        AND tenant_id  = @TenantId;
END;
GO
