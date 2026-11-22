CREATE OR ALTER PROCEDURE fin.usp_BankStatement_Get
    @CompanyId       int,
    @BankStatementId int
AS
BEGIN
    -- Un extras cu liniile lui (02.02, F2; macheta f2-02): antetul (cu fisierul, golul acceptat si soldul extrasului anterior), apoi liniile.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.BankStatement WHERE Id = @BankStatementId AND CompanyId = @CompanyId)
        RETURN;

    SELECT s.Id, s.TreasuryAccountId, a.Code AS AccountCode, a.Name AS AccountName, s.StatementNo, s.StatementDate, s.PeriodFrom, s.PeriodTo,
        s.CurrencyCode, s.OpeningBalance, s.ClosingBalance, s.Source, s.Status, s.ContinuityStatus,
        (SELECT COUNT(*) FROM fin.BankStatementLine AS l WHERE l.BankStatementId = s.Id) AS LineCount,
        (SELECT COUNT(*) FROM fin.BankStatementLine AS l WHERE l.BankStatementId = s.Id AND l.MatchStatus = 4) AS ValidatedCount,
        s.RowVersion, f.FileName, s.GapReason, s.GapAcceptedAt, u.DisplayName AS GapAcceptedByName,
        (SELECT TOP (1) p.ClosingBalance FROM fin.BankStatement AS p
         WHERE p.TreasuryAccountId = s.TreasuryAccountId AND p.PeriodTo < s.PeriodFrom ORDER BY p.PeriodTo DESC, p.Id DESC) AS PreviousClosingBalance
    FROM fin.BankStatement AS s
    JOIN fin.TreasuryAccount AS a ON a.Id = s.TreasuryAccountId
    LEFT JOIN fin.BankStatementFile AS f ON f.Id = s.BankStatementFileId
    LEFT JOIN core.[User] AS u ON u.Id = s.GapAcceptedBy
    WHERE s.Id = @BankStatementId;

    SELECT l.Id, l.LineNumber, l.BookingDate, l.ValueDate, l.Amount, l.CounterpartyName, l.CounterpartyIban, l.CounterpartyBic, l.RemittanceInfo,
        l.EndToEndId, l.BankReference, l.BankTransactionCode, l.ForeignAmount, l.ForeignCurrency, l.MatchStatus, l.PartnerId, p.Name AS PartnerName,
        l.RowVersion
    FROM fin.BankStatementLine AS l
    LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
    WHERE l.BankStatementId = @BankStatementId
    ORDER BY l.LineNumber, l.Id;
END;
