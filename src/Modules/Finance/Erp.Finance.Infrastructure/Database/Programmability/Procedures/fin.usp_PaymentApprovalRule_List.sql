CREATE OR ALTER PROCEDURE fin.usp_PaymentApprovalRule_List
    @CompanyId int
AS
BEGIN
    -- Regulile active ale firmei (F5 3.2; macheta cf-09) si rolurile care pot fi aprobatori.
    SET NOCOUNT ON;

    SELECT r.Id, r.Level, r.ThresholdRon, r.Basis, r.ApproverRoleId, ro.Code AS ApproverRoleCode, ro.Name AS ApproverRoleName
    FROM fin.PaymentApprovalRule AS r JOIN core.Role AS ro ON ro.Id = r.ApproverRoleId
    WHERE r.CompanyId = @CompanyId AND r.IsActive = 1
    ORDER BY r.Level;

    SELECT Id, Code, Name FROM core.Role ORDER BY Name;
END;
