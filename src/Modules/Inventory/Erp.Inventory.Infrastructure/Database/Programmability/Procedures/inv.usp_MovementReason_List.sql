CREATE OR ALTER PROCEDURE inv.usp_MovementReason_List
    @CompanyId int
AS
BEGIN
    -- Motivele de incarcare si descarcare ale firmei (02.05, S2 3.5): cele livrate si cele proprii.
    SET NOCOUNT ON;

    SELECT Id, Code, Name, Direction, VatTreatment, IsDeductible, RequiresAttachment, ApprovalThreshold, ApproverRoleCode, NoPosting, CostSource, IsSystem,
        IsActive
    FROM inv.MovementReason
    WHERE CompanyId IS NULL OR CompanyId = @CompanyId
    ORDER BY Direction DESC, IsSystem DESC, CodeKey;
END;
