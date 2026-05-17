CREATE OR ALTER PROCEDURE administration.usp_DeletePartnerType
    @PartnerTypeId TINYINT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1
    FROM administration.partner_types
    WHERE partner_type_id = @PartnerTypeId AND is_system = 1)
    BEGIN
        RAISERROR(N'Tipurile de sistem nu pot fi șterse.', 16, 1);
        RETURN;
    END;

    DELETE FROM administration.partner_types WHERE partner_type_id = @PartnerTypeId;
END;
