CREATE OR ALTER PROCEDURE fin.usp_ExpenseReport_Lookups
    @CompanyId int,
    @Day       date
AS
BEGIN
    -- Listele decontului: tipurile de cheltuieli in vigoare la @Day (ultimul ValidFrom pe cod) si deconturile firmei (lista ecranului).
    SET NOCOUNT ON;

    SELECT x.Id, x.Code, x.Name, x.IsPerDiem, x.DailyAmount, x.NonTaxableDailyLimit
    FROM (SELECT t.*, ROW_NUMBER() OVER (PARTITION BY t.CodeKey ORDER BY t.ValidFrom DESC) AS Rn
          FROM fin.ExpenseType AS t
          WHERE t.IsActive = 1 AND t.ValidFrom <= @Day AND (t.ValidTo IS NULL OR t.ValidTo >= @Day)) AS x
    WHERE x.Rn = 1
    ORDER BY x.IsPerDiem, x.Name;

    SELECT r.Id, r.Number, r.DocumentDate, CONCAT(e.LastName, N' ', e.FirstName) AS EmployeeName, r.CashAdvanceId, r.TotalJustified, r.Difference, r.Status
    FROM fin.ExpenseReport AS r
    JOIN core.Employee AS e ON e.Id = r.EmployeeId
    WHERE r.CompanyId = @CompanyId
    ORDER BY CASE WHEN r.Status IN (4, 5) THEN 1 ELSE 0 END, r.DocumentDate DESC, r.Id DESC;
END;
