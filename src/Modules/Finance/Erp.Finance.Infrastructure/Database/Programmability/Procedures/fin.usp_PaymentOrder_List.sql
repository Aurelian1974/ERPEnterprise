CREATE OR ALTER PROCEDURE fin.usp_PaymentOrder_List
    @CompanyId         int,
    @TreasuryAccountId int = NULL,
    @Status            tinyint = NULL,
    @Unconfirmed       bit = 0,
    @From              date = NULL,
    @To                date = NULL
AS
BEGIN
    -- Lista ordinelor de plata ale firmei (02.02, F2; macheta f2-03), cele mai noi primele, cel mult 500.
    SET NOCOUNT ON;

    SELECT TOP (500) o.Id, o.TreasuryAccountId, a.Code AS AccountCode, o.Number, o.DocumentDate, o.RequestedExecutionDate, o.Origin, o.Purpose, o.PartnerId,
        o.BeneficiaryName, o.BeneficiaryIban, o.CurrencyCode, o.Amount, o.RemittanceInfo, o.Status, o.ConfirmationStatus, o.ApprovedAt, o.PaymentBatchId,
        o.ReversalOfId, o.RowVersion
    FROM fin.PaymentOrder AS o
    JOIN fin.TreasuryAccount AS a ON a.Id = o.TreasuryAccountId
    WHERE o.CompanyId = @CompanyId
      AND (@TreasuryAccountId IS NULL OR o.TreasuryAccountId = @TreasuryAccountId)
      AND (@Status IS NULL OR o.Status = @Status)
      AND (@Unconfirmed = 0 OR o.ConfirmationStatus = 1)
      AND (@From IS NULL OR o.DocumentDate >= @From)
      AND (@To IS NULL OR o.DocumentDate <= @To)
    ORDER BY o.DocumentDate DESC, o.Id DESC;
END;
