CREATE OR ALTER PROCEDURE fin.usp_BankReconciliation_Get
    @CompanyId         int,
    @TreasuryAccountId int
AS
BEGIN
    -- Ecranul Extras de cont (02.02, F2 4.4; macheta f2-02): antetul (ultimul extras, soldul din extras, OP-urile neconfirmate, soldul
    -- scriptic), liniile nevalidate ale contului (cel mult 1.000, cele mai vechi primele), OP-urile validate neconfirmate,
    -- tipurile de operatiuni bancare si conturile bancare ale firmei (alegerea contului si contul pereche).
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.TreasuryAccount WHERE Id = @TreasuryAccountId AND CompanyId = @CompanyId AND Kind IN (1, 3, 5))
        RETURN;

    DECLARE @Unconfirmed decimal(19,2) = ISNULL((SELECT SUM(Amount) FROM fin.PaymentOrder
        WHERE TreasuryAccountId = @TreasuryAccountId AND Status = 2 AND ConfirmationStatus = 1 AND ReversalOfId IS NULL), 0);

    SELECT TOP (1) a.Id AS TreasuryAccountId, a.Code AS AccountCode, a.Name AS AccountName, a.CurrencyCode, s.Id AS BankStatementId, s.StatementNo,
        s.StatementDate, s.ClosingBalance, @Unconfirmed AS UnconfirmedAmount,
        (SELECT COUNT(*) FROM fin.BankStatementLine AS l WHERE l.TreasuryAccountId = a.Id AND l.MatchStatus <> 4) AS OpenLineCount
    FROM fin.TreasuryAccount AS a
    LEFT JOIN fin.BankStatement AS s ON s.TreasuryAccountId = a.Id
    WHERE a.Id = @TreasuryAccountId
    ORDER BY s.PeriodTo DESC, s.Id DESC;

    SELECT TOP (1000) l.Id, l.BankStatementId, s.StatementNo, l.LineNumber, l.BookingDate, l.Amount, l.CounterpartyName, l.CounterpartyIban, l.RemittanceInfo,
        l.MatchStatus, l.PartnerId, p.Name AS PartnerName, s.ContinuityStatus,
        (SELECT COUNT(*) FROM fin.MatchSuggestion AS m WHERE m.BankStatementLineId = l.Id) AS SuggestionCount, l.RowVersion
    FROM fin.BankStatementLine AS l
    JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId
    LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
    WHERE l.TreasuryAccountId = @TreasuryAccountId AND l.MatchStatus <> 4
    ORDER BY l.BookingDate, l.Id;

    SELECT TOP (200) o.Id, o.Number, o.DocumentDate, o.BeneficiaryName, o.Amount, o.ApprovedAt, o.PaymentBatchId, b.Number AS BatchNumber, b.Status AS BatchStatus
    FROM fin.PaymentOrder AS o
    LEFT JOIN fin.PaymentBatch AS b ON b.Id = o.PaymentBatchId
    WHERE o.TreasuryAccountId = @TreasuryAccountId AND o.Status = 2 AND o.ConfirmationStatus = 1 AND o.ReversalOfId IS NULL
    ORDER BY o.DocumentDate, o.Id;

    SELECT Id, Code, Name, Direction, RequiresPartner, RequiresEmployee, RequiresCounterAccount
    FROM fin.TreasuryOperationType WHERE IsActive = 1 AND AppliesToBank = 1 ORDER BY Direction, Name;

    SELECT Id, Code, Name FROM fin.TreasuryAccount
    WHERE CompanyId = @CompanyId AND IsActive = 1 AND Kind IN (1, 3, 5) ORDER BY Code;
END;
