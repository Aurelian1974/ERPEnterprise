CREATE OR ALTER PROCEDURE fin.usp_BankStatement_PreviousClosing
    @TreasuryAccountId int,
    @Before            date
AS
BEGIN
    -- Soldul final al ultimului extras al contului incheiat inainte de @Before (02.02, F2 4.1: soldul initial al unui CSV fara sold).
    SET NOCOUNT ON;

    SELECT TOP (1) ClosingBalance
    FROM fin.BankStatement
    WHERE TreasuryAccountId = @TreasuryAccountId AND PeriodTo < @Before
    ORDER BY PeriodTo DESC, Id DESC;
END;
