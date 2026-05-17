CREATE OR ALTER PROCEDURE administration.usp_UpdatePartner
    @Id                 UNIQUEIDENTIFIER,
    @TenantId           UNIQUEIDENTIFIER,
    @Code               NVARCHAR(20),
    @Name               NVARCHAR(200),
    @Cui                NVARCHAR(20)    = NULL,
    @RegistrationNumber NVARCHAR(50)    = NULL,
    @LegalForm          NVARCHAR(50)    = NULL,
    @PartnerTypeId      TINYINT         = NULL,
    @IsVatPayer         BIT             = 0,
    @Phone              NVARCHAR(30)    = NULL,
    @Email              NVARCHAR(200)   = NULL,
    @Notes              NVARCHAR(1000)  = NULL,
    @IsActive           BIT             = 1,
    @UpdatedBy          UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE administration.partners
    SET
        code                = @Code,
        name                = @Name,
        cui                 = @Cui,
        registration_number = @RegistrationNumber,
        legal_form          = @LegalForm,
        partner_type_id     = @PartnerTypeId,
        is_vat_payer        = @IsVatPayer,
        phone               = @Phone,
        email               = @Email,
        notes               = @Notes,
        is_active           = @IsActive,
        updated_at          = SYSUTCDATETIME(),
        updated_by          = @UpdatedBy
    WHERE id        = @Id
        AND tenant_id = @TenantId;
END;
GO
