CREATE OR ALTER PROCEDURE administration.usp_GetPartnerDomainById
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
    WHERE p.id        = @Id
        AND p.tenant_id = @TenantId;
END;
GO
