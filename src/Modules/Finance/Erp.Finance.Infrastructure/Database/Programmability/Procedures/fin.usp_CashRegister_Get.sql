CREATE OR ALTER PROCEDURE fin.usp_CashRegister_Get
    @CompanyId         int,
    @TreasuryAccountId int,
    @From              date,
    @To                date
AS
BEGIN
    -- Registrul de casa (02.02, F1 3.4 si 10; macheta f1-04): zilele casei din interval (sold de inceput, incasari, plati, sold, starea,
    -- paginile, inchiderea si redeschiderea) si documentele validate ale zilelor, in ordinea validarii. Soldul dupa fiecare document se
    -- calculeaza in C# din soldul de inceput al zilei.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId AND Kind = 2)
        RETURN;

    SELECT r.[Day], r.OpeningBalance, r.TotalIn, r.TotalOut, r.ClosingBalance, r.Status, r.FirstPageNo, r.LastPageNo, r.ClosedAt,
           c.DisplayName AS ClosedByName, r.ReopenedAt, o.DisplayName AS ReopenedByName, r.ReopenReason,
           (SELECT COUNT(*) FROM fin.CashDocument AS d WHERE d.TreasuryAccountId = r.TreasuryAccountId AND d.DocumentDate = r.[Day] AND d.Status = 1)
               AS DraftCount
    FROM fin.CashRegisterDay AS r
    LEFT JOIN core.[User] AS c ON c.Id = r.ClosedBy
    LEFT JOIN core.[User] AS o ON o.Id = r.ReopenedBy
    WHERE r.TreasuryAccountId = @TreasuryAccountId AND r.[Day] BETWEEN @From AND @To
    ORDER BY r.[Day];

    SELECT d.Id, d.DocumentDate, d.Kind, d.Number, COALESCE(p.Name, CONCAT(e.LastName, N' ', e.FirstName), d.PersonName) AS PersonName,
           d.Description, d.TotalAmount, d.Status, d.ReversalOfId, d.ValidatedAt
    FROM fin.CashDocument AS d
    LEFT JOIN core.Partner AS p ON p.Id = d.PartnerId
    LEFT JOIN core.Employee AS e ON e.Id = d.EmployeeId
    WHERE d.TreasuryAccountId = @TreasuryAccountId AND d.DocumentDate BETWEEN @From AND @To AND d.Status IN (2, 3)
    ORDER BY d.DocumentDate, d.ValidatedAt, d.Id;
END;
