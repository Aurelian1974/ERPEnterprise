CREATE OR ALTER PROCEDURE administration.usp_ApplyAnafData
    @Id                    UNIQUEIDENTIFIER,
    @TenantId              UNIQUEIDENTIFIER,
    @IsVatPayer            BIT,
    @RegistrationNumber    NVARCHAR(50)     = NULL,
    @LegalForm             NVARCHAR(150)    = NULL,
    @Phone                 NVARCHAR(30)     = NULL,
    @AnafVerifiedAt        DATETIME2(7),
    @UpdatedBy             UNIQUEIDENTIFIER,
    -- Sediu social address (optional)
    @SediuSocialStreet     NVARCHAR(300)    = NULL,
    @SediuSocialCity       NVARCHAR(100)    = NULL,
    @SediuSocialCounty     NVARCHAR(100)    = NULL,
    @SediuSocialPostalCode NVARCHAR(20)     = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE administration.partners
    SET
        is_vat_payer        = @IsVatPayer,
        registration_number = COALESCE(@RegistrationNumber, registration_number),
        legal_form          = COALESCE(@LegalForm, legal_form),
        phone               = COALESCE(@Phone, phone),
        anaf_verified_at    = @AnafVerifiedAt,
        updated_at          = SYSUTCDATETIME(),
        updated_by          = @UpdatedBy
    WHERE id        = @Id
        AND tenant_id = @TenantId;

    -- Upsert "Sediu social" address only when ANAF returns city data
    IF @SediuSocialCity IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM administration.partner_addresses
            WHERE partner_id  = @Id
              AND tenant_id   = @TenantId
              AND address_type = N'Sediu social'
        )
        BEGIN
            UPDATE administration.partner_addresses
            SET
                street      = @SediuSocialStreet,
                city        = @SediuSocialCity,
                county      = @SediuSocialCounty,
                postal_code = @SediuSocialPostalCode
            WHERE partner_id  = @Id
              AND tenant_id   = @TenantId
              AND address_type = N'Sediu social';
        END
        ELSE
        BEGIN
            INSERT INTO administration.partner_addresses
                (partner_id, tenant_id, address_type, street, city, county, postal_code, country, is_primary)
            VALUES
                (@Id, @TenantId, N'Sediu social', @SediuSocialStreet, @SediuSocialCity,
                 @SediuSocialCounty, @SediuSocialPostalCode, N'România', 0);
        END
    END
END;
GO
