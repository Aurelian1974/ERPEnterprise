CREATE OR ALTER PROCEDURE administration.usp_UpsertPartnerAddress
    @Id          BIGINT           = NULL,
    @PartnerId   UNIQUEIDENTIFIER,
    @TenantId    UNIQUEIDENTIFIER,
    @AddressType NVARCHAR(50),
    @Street      NVARCHAR(300),
    @City        NVARCHAR(100),
    @County      NVARCHAR(100)    = NULL,
    @PostalCode  NVARCHAR(20)     = NULL,
    @Country     NVARCHAR(100)    = N'România',
    @IsPrimary   BIT              = 0
AS
BEGIN
    SET NOCOUNT ON;

    IF @Id IS NULL
    BEGIN
        INSERT INTO administration.partner_addresses
            (partner_id, tenant_id, address_type, street, city, county, postal_code, country, is_primary)
        VALUES
            (@PartnerId, @TenantId, @AddressType, @Street, @City, @County, @PostalCode, @Country, @IsPrimary);
    END
    ELSE
    BEGIN
        UPDATE administration.partner_addresses
        SET
            address_type = @AddressType,
            street       = @Street,
            city         = @City,
            county       = @County,
            postal_code  = @PostalCode,
            country      = @Country,
            is_primary   = @IsPrimary
        WHERE id         = @Id
            AND partner_id = @PartnerId
            AND tenant_id  = @TenantId;
    END;
END;
GO
