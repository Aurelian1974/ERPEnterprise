CREATE OR ALTER PROCEDURE fin.usp_BankStatement_List
    @CompanyId         int,
    @TreasuryAccountId int = NULL,
    @From              date = NULL,
    @To                date = NULL
AS
BEGIN
    -- Lista extraselor firmei (02.02, F2; macheta f2-01), cele mai noi primele, cel mult 500.
    SET NOCOUNT ON;

    SELECT TOP (500) s.Id, s.TreasuryAccountId, a.Code AS AccountCode, a.Name AS AccountName, s.StatementNo, s.StatementDate, s.PeriodFrom, s.PeriodTo,
        s.CurrencyCode, s.OpeningBalance, s.ClosingBalance, s.Source, s.Status, s.ContinuityStatus,
        (SELECT COUNT(*) FROM fin.BankStatementLine AS l WHERE l.BankStatementId = s.Id) AS LineCount,
        (SELECT COUNT(*) FROM fin.BankStatementLine AS l WHERE l.BankStatementId = s.Id AND l.MatchStatus = 4) AS ValidatedCount,
        s.RowVersion
    FROM fin.BankStatement AS s
    JOIN fin.TreasuryAccount AS a ON a.Id = s.TreasuryAccountId
    WHERE s.CompanyId = @CompanyId
      AND (@TreasuryAccountId IS NULL OR s.TreasuryAccountId = @TreasuryAccountId)
      AND (@From IS NULL OR s.StatementDate >= @From)
      AND (@To IS NULL OR s.StatementDate <= @To)
    ORDER BY s.StatementDate DESC, s.Id DESC;
END;
