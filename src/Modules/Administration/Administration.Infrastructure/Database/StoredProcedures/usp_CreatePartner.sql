CREATE OR ALTER PROCEDURE administration.usp_CreatePartner
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
    @AnafVerifiedAt     DATETIME2(7)    = NULL,
    @CreatedBy          UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO administration.partners
        (
        id, tenant_id, code, name, cui, registration_number,
        legal_form, partner_type_id, is_vat_payer, phone, email,
        notes, anaf_verified_at, created_at, created_by
        )
    VALUES
        (
            @Id, @TenantId, @Code, @Name, @Cui, @RegistrationNumber,
            @LegalForm, @PartnerTypeId, @IsVatPayer, @Phone, @Email,
            @Notes, @AnafVerifiedAt, SYSUTCDATETIME(), @CreatedBy
    );
END;
GO
