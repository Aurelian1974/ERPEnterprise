CREATE OR ALTER PROCEDURE fin.usp_GlAccountRole_List
    @CompanyId int
AS
BEGIN
    -- Rolurile conturilor (C1 3.2): atribuirile firmei si cele implicite ale instalarii (CompanyId NULL), cu contul.
    SET NOCOUNT ON;

    SELECT r.Id, r.CompanyId, r.Role, r.AccountId, a.Code AS AccountCode, a.Name AS AccountName, r.PartnerKind, r.CurrencyCode, r.ValidFrom, r.ValidTo
    FROM fin.GlAccountRoleAssignment AS r
    JOIN fin.GlAccount AS a ON a.Id = r.AccountId
    WHERE r.CompanyId IS NULL OR r.CompanyId = @CompanyId
    ORDER BY r.Role, CASE WHEN r.CompanyId IS NULL THEN 1 ELSE 0 END, r.PartnerKind, r.CurrencyCode, r.ValidFrom;
END;
