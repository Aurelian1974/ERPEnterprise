CREATE OR ALTER PROCEDURE fin.usp_CashAdvance_List
    @CompanyId int,
    @Filter    tinyint,
    @Today     date
AS
BEGIN
    -- Lista avansurilor spre decontare (macheta f1-05): 0 toate, 1 deschise (de platit, acordate, decontate partial, depasite),
    -- 2 depasite (marcate sau cu termenul trecut), 3 decontate sau stornate.
    SET NOCOUNT ON;

    SELECT a.Id, a.EmployeeId, CONCAT(e.LastName, N' ', e.FirstName) AS EmployeeName, a.Purpose, a.CurrencyCode, a.Amount, a.SettledAmount,
           a.ReturnedAmount, a.PaidDifference, a.Balance, a.SettlementDueDate, a.TripStartDate, a.TripEndDate, a.Status, a.Description,
           g.Number AS GrantedByNumber, r.Id AS ExpenseReportId, r.Number AS ExpenseReportNumber,
           CASE WHEN a.Status IN (1, 2, 4) AND a.SettlementDueDate < @Today THEN DATEDIFF(day, a.SettlementDueDate, @Today) ELSE 0 END AS DaysOverdue,
           a.RowVersion
    FROM fin.CashAdvance AS a
    JOIN core.Employee AS e ON e.Id = a.EmployeeId
    LEFT JOIN fin.CashDocument AS g ON g.Id = a.GrantedByCashDocumentId
    LEFT JOIN fin.ExpenseReport AS r ON r.CashAdvanceId = a.Id AND r.Status <> 5
    WHERE a.CompanyId = @CompanyId
      AND (@Filter = 0
           OR (@Filter = 1 AND a.Status IN (0, 1, 2, 4))
           OR (@Filter = 2 AND (a.Status = 4 OR (a.Status IN (1, 2) AND a.SettlementDueDate < @Today)))
           OR (@Filter = 3 AND a.Status IN (3, 5)))
    ORDER BY CASE WHEN a.Status IN (3, 5) THEN 1 ELSE 0 END, a.SettlementDueDate, a.Id;
END;
