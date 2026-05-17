CREATE OR ALTER PROCEDURE administration.usp_GetPartnerTypeById
    @PartnerTypeId TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        pt.partner_type_id            AS PartnerTypeId,
        pt.code                       AS Code,
        pt.name                       AS Name,
        pt.description                AS Description,
        pt.is_system                  AS IsSystem,
        pt.is_active                  AS IsActive,
        pt.affects_issued_invoices    AS AffectsIssuedInvoices,
        pt.affects_received_invoices  AS AffectsReceivedInvoices,
        pt.sort_order                 AS SortOrder,
        pt.created_at                 AS CreatedAt,
        pt.created_by                 AS CreatedBy,
        pt.updated_at                 AS UpdatedAt,
        pt.updated_by                 AS UpdatedBy
    FROM administration.partner_types pt
    WHERE pt.partner_type_id = @PartnerTypeId;
END;
