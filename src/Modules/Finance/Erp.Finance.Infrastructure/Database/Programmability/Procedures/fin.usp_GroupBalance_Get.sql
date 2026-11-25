CREATE OR ALTER PROCEDURE fin.usp_GroupBalance_Get
    @RunId  int
AS
BEGIN
    -- O rulare a balantei de grup (macheta c5-05): antetul, firmele si liniile pe cont, firma si fel (sold, eliminare).
    SET NOCOUNT ON;
    SELECT r.Id, r.ChartId, c.Code AS ChartCode, r.PeriodYear, r.PeriodMonth, r.IsFinal, r.RunAt FROM fin.GroupBalanceRun AS r
    JOIN fin.ChartOfAccounts AS c ON c.Id = r.ChartId WHERE r.Id = @RunId;
    SELECT co.Id, co.Code, co.Name FROM core.Company AS co WHERE co.Id IN (SELECT CompanyId FROM fin.ChartOfAccountsCompany WHERE ChartId = (SELECT ChartId FROM fin.GroupBalanceRun WHERE Id = @RunId))
    ORDER BY co.Code;
    SELECT l.CompanyId, l.Kind, a.Code AS AccountCode, a.Name AS AccountName, l.DebitRon, l.CreditRon
    FROM fin.GroupBalanceRunLine AS l JOIN fin.GlAccount AS a ON a.Id = l.AccountId WHERE l.RunId = @RunId ORDER BY a.Code, l.CompanyId, l.Kind;
END;
