CREATE OR ALTER PROCEDURE administration.usp_GetPartnerById
    @Id       UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        p.id                  AS Id,
        p.tenant_id           AS TenantId,
        p.code                AS Code,
        p.name                AS Name,
        p.cui                 AS Cui,
        p.registration_number AS RegistrationNumber,
        p.legal_form          AS LegalForm,
        p.partner_type_id     AS PartnerTypeId,
        pt.name               AS PartnerTypeName,
        p.is_vat_payer        AS IsVatPayer,
        p.phone               AS Phone,
        p.email               AS Email,
        p.is_active           AS IsActive,
        p.notes               AS Notes,
        p.created_at          AS CreatedAt,
        p.created_by          AS CreatedBy,
        p.updated_at          AS UpdatedAt,
        p.updated_by          AS UpdatedBy,
        p.anaf_verified_at    AS AnafVerifiedAt
    FROM administration.partners p
        LEFT JOIN administration.partner_types pt
        ON pt.partner_type_id = p.partner_type_id
    WHERE p.id        = @Id
        AND p.tenant_id = @TenantId;

    SELECT
        pa.id           AS Id,
        pa.address_type AS AddressType,
        pa.street       AS Street,
        pa.city         AS City,
        pa.county       AS County,
        pa.postal_code  AS PostalCode,
        pa.country      AS Country,
        pa.is_primary   AS IsPrimary
    FROM administration.partner_addresses pa
    WHERE pa.partner_id = @Id
        AND pa.tenant_id  = @TenantId
    ORDER BY pa.is_primary DESC, pa.id;

    SELECT
        pc.id         AS Id,
        pc.full_name  AS FullName,
        pc.position   AS Position,
        pc.phone      AS Phone,
        pc.email      AS Email,
        pc.is_primary AS IsPrimary
    FROM administration.partner_contacts pc
    WHERE pc.partner_id = @Id
        AND pc.tenant_id  = @TenantId
    ORDER BY pc.is_primary DESC, pc.id;

    SELECT
        pb.id         AS Id,
        pb.iban       AS Iban,
        pb.bank_name  AS BankName,
        pb.currency   AS Currency,
        pb.is_default AS IsDefault
    FROM administration.partner_bank_accounts pb
    WHERE pb.partner_id = @Id
        AND pb.tenant_id  = @TenantId
    ORDER BY pb.is_default DESC, pb.id;
END;
GO
