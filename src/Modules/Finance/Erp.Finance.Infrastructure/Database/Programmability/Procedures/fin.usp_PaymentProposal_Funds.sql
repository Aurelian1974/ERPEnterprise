CREATE OR ALTER PROCEDURE fin.usp_PaymentProposal_Funds
    @CompanyId          int,
    @ExcludeProposalId  int = NULL
AS
BEGIN
    -- Banii disponibili pe conturile bancare active (F5 4.1 pasul 4): soldul scriptic (soldul ultimului extras minus OP-urile validate
    -- neconfirmate), descoperitul permis si sumele deja propuse in alte propuneri deschise, in moneda contului.
    SET NOCOUNT ON;

    SELECT a.Id AS TreasuryAccountId, a.Code, a.CurrencyCode, ISNULL(s.ClosingBalance, 0) - ISNULL(u.Amount, 0) AS BookBalance, ISNULL(a.OverdraftLimit, 0) AS OverdraftLimit,
        ISNULL(pp.Amount, 0) AS AlreadyProposed
    FROM fin.TreasuryAccount AS a
    OUTER APPLY (SELECT TOP (1) x.ClosingBalance FROM fin.BankStatement AS x WHERE x.TreasuryAccountId = a.Id ORDER BY x.StatementDate DESC, x.Id DESC) AS s
    OUTER APPLY (SELECT SUM(o.Amount) AS Amount FROM fin.PaymentOrder AS o
                 WHERE o.TreasuryAccountId = a.Id AND o.Status = 2 AND o.ConfirmationStatus = 1 AND o.ReversalOfId IS NULL) AS u
    OUTER APPLY (SELECT SUM(l.ProposedAmount) AS Amount FROM fin.PaymentProposalLine AS l
                 WHERE l.TreasuryAccountId = a.Id AND l.IsActive = 1 AND (@ExcludeProposalId IS NULL OR l.PaymentProposalId <> @ExcludeProposalId)) AS pp
    WHERE a.CompanyId = @CompanyId AND a.IsActive = 1 AND a.Kind IN (1, 3, 5)
    ORDER BY a.Code;
END;
