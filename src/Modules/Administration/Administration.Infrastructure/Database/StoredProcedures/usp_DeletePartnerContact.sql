CREATE OR ALTER PROCEDURE administration.usp_DeletePartnerContact
    @Id        BIGINT,
    @PartnerId UNIQUEIDENTIFIER,
    @TenantId  UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    DELETE FROM administration.partner_contacts
    WHERE id         = @Id
        AND partner_id = @PartnerId
        AND tenant_id  = @TenantId;
END;
GO
