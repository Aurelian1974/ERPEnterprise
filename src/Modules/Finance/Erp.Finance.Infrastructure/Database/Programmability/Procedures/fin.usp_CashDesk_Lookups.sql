CREATE OR ALTER PROCEDURE fin.usp_CashDesk_Lookups
    @CompanyId int,
    @UserId    int,
    @ViewAll   bit,
    @Day       date
AS
BEGIN
    -- Listele ecranului Casa (macheta f1-02): casele active pe care utilizatorul le vede (atribuite azi sau toate, cu Fin.Cash.ViewAll),
    -- cu dreptul lui de validare; tipurile de operatiuni de casa active; conturile corespondente (celelalte conturi active ale firmei);
    -- angajatii activi ai firmei.
    SET NOCOUNT ON;

    SELECT a.Id, a.Code, a.Name, a.CurrencyCode,
           CAST(CASE WHEN u.CanValidate = 1 THEN 1 ELSE 0 END AS bit) AS CanValidate
    FROM fin.TreasuryAccount AS a
    OUTER APPLY (SELECT TOP (1) x.CanValidate FROM fin.TreasuryAccountUser AS x
                 WHERE x.TreasuryAccountId = a.Id AND x.UserId = @UserId
                   AND (x.ValidFrom IS NULL OR x.ValidFrom <= @Day) AND (x.ValidTo IS NULL OR x.ValidTo >= @Day)) AS u
    WHERE a.CompanyId = @CompanyId AND a.Kind = 2 AND a.IsActive = 1 AND (@ViewAll = 1 OR u.CanValidate IS NOT NULL)
    ORDER BY a.CodeKey;

    SELECT t.Id, t.Code, t.Name, t.Direction, t.RequiresPartner, t.RequiresEmployee, t.RequiresCounterAccount, t.CashLimitCategory, t.SettlementKind
    FROM fin.TreasuryOperationType AS t
    WHERE t.IsActive = 1 AND t.AppliesToCash = 1
    ORDER BY t.Direction, t.Name;

    SELECT a.Id, a.Code, a.Name
    FROM fin.TreasuryAccount AS a
    WHERE a.CompanyId = @CompanyId AND a.IsActive = 1
    ORDER BY a.Kind, a.CodeKey;

    SELECT e.Id, CONCAT(e.LastName, N' ', e.FirstName) AS Name, e.EmployeeNo
    FROM core.Employee AS e
    WHERE e.CompanyId = @CompanyId AND e.IsActive = 1
    ORDER BY e.LastName, e.FirstName;
END;
