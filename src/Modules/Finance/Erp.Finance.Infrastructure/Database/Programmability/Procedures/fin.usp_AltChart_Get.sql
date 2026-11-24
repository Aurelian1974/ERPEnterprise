CREATE OR ALTER PROCEDURE fin.usp_AltChart_Get
    @Id int
AS
BEGIN
    -- Un plan de conturi (C1 3.1; macheta c1-02): antetul, cu numarul de conturi si de reguli, si firmele lui (fara rand daca nu exista).
    SET NOCOUNT ON;

    SELECT c.Id, c.Code, c.Name, c.Kind, c.Purpose, c.LanguageCode, c.RequireCompleteMapping, c.IsActive, c.RowVersion,
           (SELECT COUNT(*) FROM fin.GlAccount AS a WHERE a.ChartId = c.Id) AS AccountCount,
           (SELECT COUNT(*) FROM fin.AccountMappingRule AS r WHERE r.TargetChartId = c.Id) AS RuleCount
    FROM fin.ChartOfAccounts AS c
    WHERE c.Id = @Id;

    SELECT x.CompanyId, co.Name AS CompanyName
    FROM fin.ChartOfAccountsCompany AS x
    JOIN core.Company AS co ON co.Id = x.CompanyId
    WHERE x.ChartId = @Id
    ORDER BY co.Name;
END;
