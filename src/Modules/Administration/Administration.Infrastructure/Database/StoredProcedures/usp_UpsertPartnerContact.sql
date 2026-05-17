CREATE OR ALTER PROCEDURE administration.usp_UpsertPartnerContact
    @Id        BIGINT           = NULL,
    @PartnerId UNIQUEIDENTIFIER,
    @TenantId  UNIQUEIDENTIFIER,
    @FullName  NVARCHAR(200),
    @Position  NVARCHAR(100)    = NULL,
    @Phone     NVARCHAR(30)     = NULL,
    @Email     NVARCHAR(200)    = NULL,
    @IsPrimary BIT              = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF @Id IS NULL
    BEGIN
        INSERT INTO administration.partner_contacts
            (partner_id, tenant_id, full_name, position, phone, email, is_primary)
        VALUES
            (@PartnerId, @TenantId, @FullName, @Position, @Phone, @Email, @IsPrimary);
    END
    ELSE
    BEGIN
        UPDATE administration.partner_contacts
        SET
            full_name  = @FullName,
            position   = @Position,
            phone      = @Phone,
            email      = @Email,
            is_primary = @IsPrimary
        WHERE id         = @Id
            AND partner_id = @PartnerId
            AND tenant_id  = @TenantId;
    END;
END;
GO
