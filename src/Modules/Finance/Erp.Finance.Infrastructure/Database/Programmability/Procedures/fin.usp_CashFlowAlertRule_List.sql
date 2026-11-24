CREATE OR ALTER PROCEDURE fin.usp_CashFlowAlertRule_List
    @CompanyId int
AS
BEGIN
    -- Regulile de alerta de cash-flow ale firmei (F5 3.7).
    SET NOCOUNT ON;

    SELECT r.Id, r.TreasuryAccountId, a.Code AS TreasuryAccountCode, r.MinBalanceRon, r.HorizonDays, r.NotifyRoleId, ro.Name AS NotifyRoleName, r.IsActive,
           r.LastAlertedOn
    FROM fin.CashFlowAlertRule AS r
    LEFT JOIN fin.TreasuryAccount AS a ON a.Id = r.TreasuryAccountId
    LEFT JOIN core.Role AS ro ON ro.Id = r.NotifyRoleId
    WHERE r.CompanyId = @CompanyId
    ORDER BY CASE WHEN r.TreasuryAccountId IS NULL THEN 0 ELSE 1 END, a.Code, r.Id;
END;
