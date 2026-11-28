CREATE OR ALTER PROCEDURE fin.usp_ChartOfAccounts_List
AS
BEGIN
    -- Planurile de conturi (C1 3.1; macheta c1-02): statutarul primul, apoi alternativele, cu numarul de conturi, de reguli si de firme.
    SET NOCOUNT ON;

    SELECT c.Id, c.Code, c.Name, c.Kind, c.Purpose, c.LanguageCode, c.RequireCompleteMapping, c.IsActive,
           (SELECT COUNT(*) FROM fin.GlAccount AS a WHERE a.ChartId = c.Id) AS AccountCount,
           (SELECT COUNT(*) FROM fin.AccountMappingRule AS r WHERE r.TargetChartId = c.Id) AS RuleCount,
           (SELECT COUNT(*) FROM fin.ChartOfAccountsCompany AS x WHERE x.ChartId = c.Id) AS CompanyCount
    FROM fin.ChartOfAccounts AS c
    ORDER BY c.Kind, c.Code;
END;
