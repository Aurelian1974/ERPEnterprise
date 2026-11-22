CREATE OR ALTER PROCEDURE fin.usp_CardSettlement_List
    @CompanyId int,
    @Status    tinyint = NULL
AS
BEGIN
    -- Decontarile de card ale firmei (02.02, F2 3.7), cele mai noi primele, cel mult 500.
    SET NOCOUNT ON;

    SELECT TOP (500) c.Id, c.AcquirerCode, c.SalesDate, c.GrossAmount, c.ExpectedSettlementDate, c.SourceModule, c.SourceRef, c.SettledAmount, c.FeeAmount,
        c.Status, c.BankStatementLineId, c.Note, c.RowVersion
    FROM fin.CardSettlementExpected AS c
    WHERE c.CompanyId = @CompanyId AND (@Status IS NULL OR c.Status = @Status)
    ORDER BY c.SalesDate DESC, c.Id DESC;
END;
