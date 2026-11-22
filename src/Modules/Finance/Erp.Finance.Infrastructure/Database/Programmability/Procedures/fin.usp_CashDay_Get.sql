CREATE OR ALTER PROCEDURE fin.usp_CashDay_Get
    @CompanyId         int,
    @TreasuryAccountId int,
    @Day               date
AS
BEGIN
    -- Ecranul Casa (macheta f1-02): ziua aleasa a casei (sold de inceput, incasari, plati, sold, starea), prima zi deschisa si documentele
    -- zilei in ordinea validarii (ciornele la sfarsit). O zi fara activitate are soldul de inceput = soldul final al zilei anterioare.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId)
        RETURN;

    SELECT a.Id AS TreasuryAccountId, a.Code, a.Name, a.CurrencyCode, a.MaxClosingBalance, @Day AS [Day],
           ISNULL(r.OpeningBalance, ISNULL(prev.ClosingBalance, 0)) AS OpeningBalance, ISNULL(r.TotalIn, 0) AS TotalIn,
           ISNULL(r.TotalOut, 0) AS TotalOut, ISNULL(r.ClosingBalance, ISNULL(prev.ClosingBalance, 0)) AS ClosingBalance,
           ISNULL(r.Status, 1) AS DayStatus, r.ClosedAt,
           (SELECT MIN(x.[Day]) FROM fin.CashRegisterDay AS x WHERE x.TreasuryAccountId = a.Id AND x.Status = 1) AS FirstOpenDay,
           (SELECT MAX(x.[Day]) FROM fin.CashRegisterDay AS x WHERE x.TreasuryAccountId = a.Id AND x.Status = 2) AS LastClosedDay
    FROM fin.TreasuryAccount AS a
    LEFT JOIN fin.CashRegisterDay AS r ON r.TreasuryAccountId = a.Id AND r.[Day] = @Day
    OUTER APPLY (SELECT TOP (1) p.ClosingBalance FROM fin.CashRegisterDay AS p
                 WHERE p.TreasuryAccountId = a.Id AND p.[Day] < @Day ORDER BY p.[Day] DESC) AS prev
    WHERE a.Id = @TreasuryAccountId;

    SELECT d.Id, d.Kind, d.Number, d.DocumentDate, d.Status, COALESCE(p.Name, CONCAT(e.LastName, N' ', e.FirstName), d.PersonName) AS PersonName,
           d.TotalAmount, d.TotalAmountRon, d.Description, d.ReversalOfId, d.ReversedById, d.ValidatedAt,
           (SELECT TOP (1) t.Name FROM fin.CashDocumentLine AS l JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
            WHERE l.CashDocumentId = d.Id ORDER BY l.Amount DESC) AS OperationName
    FROM fin.CashDocument AS d
    LEFT JOIN core.Partner AS p ON p.Id = d.PartnerId
    LEFT JOIN core.Employee AS e ON e.Id = d.EmployeeId
    WHERE d.TreasuryAccountId = @TreasuryAccountId AND d.DocumentDate = @Day
    ORDER BY CASE WHEN d.Status = 1 THEN 1 ELSE 0 END, d.ValidatedAt, d.Id;
END;
