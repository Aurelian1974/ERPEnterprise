CREATE OR ALTER PROCEDURE fin.usp_PaymentBatch_List
    @CompanyId         int,
    @TreasuryAccountId int = NULL
AS
BEGIN
    -- Loturile de plati ale firmei (02.02, F2; macheta f2-03), cele mai noi primele, cel mult 200.
    SET NOCOUNT ON;

    SELECT TOP (200) b.Id, b.TreasuryAccountId, a.Code AS AccountCode, b.Number, b.Format, b.OrderCount, b.TotalAmount, b.Status, b.ExportCount, b.ExportedAt,
        b.RowVersion
    FROM fin.PaymentBatch AS b
    JOIN fin.TreasuryAccount AS a ON a.Id = b.TreasuryAccountId
    WHERE b.CompanyId = @CompanyId AND (@TreasuryAccountId IS NULL OR b.TreasuryAccountId = @TreasuryAccountId)
    ORDER BY b.Id DESC;
END;
