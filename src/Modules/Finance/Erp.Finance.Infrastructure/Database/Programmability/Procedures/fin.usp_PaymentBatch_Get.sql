CREATE OR ALTER PROCEDURE fin.usp_PaymentBatch_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Un lot de plati cu tot ce trebuie exportului (02.02, F2 3.6 si 4.2): antetul (firma platitoare, IBAN-ul contului, profilul bancar),
    -- apoi OP-urile lui.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.PaymentBatch WHERE Id = @Id AND CompanyId = @CompanyId)
        RETURN;

    SELECT b.Id, b.TreasuryAccountId, a.Code AS AccountCode, a.CurrencyCode, ba.Iban AS AccountIban, p.BankBic, p.PaymentFormat, p.PaymentMapping,
        ISNULL(p.MaxRemittanceLength, 140) AS MaxRemittanceLength, c.Name AS CompanyName, c.FiscalCode AS CompanyFiscalCode, b.Number, b.Format, b.OrderCount,
        b.TotalAmount, b.Status, b.ExportCount, b.ExportedAt, eu.DisplayName AS ExportedByName, b.FileName, b.FileSha256, b.LastReExportReason, b.CancelledAt,
        b.CancelReason, b.RowVersion
    FROM fin.PaymentBatch AS b
    JOIN fin.TreasuryAccount AS a ON a.Id = b.TreasuryAccountId
    JOIN core.Company AS c ON c.Id = b.CompanyId
    LEFT JOIN core.CompanyBankAccount AS ba ON ba.Id = a.CompanyBankAccountId
    LEFT JOIN fin.BankFormatProfile AS p ON p.Id = a.BankFormatProfileId
    LEFT JOIN core.[User] AS eu ON eu.Id = b.ExportedBy
    WHERE b.Id = @Id;

    SELECT o.Id, o.Number, o.Status, o.ReversalOfId, o.ConfirmationStatus, o.ApprovedAt, o.Origin, o.PartnerId, o.BeneficiaryName, o.BeneficiaryIban,
        o.BeneficiaryBic, o.Amount, o.RemittanceInfo, o.EndToEndId, o.RequestedExecutionDate, o.IsUrgent, o.RowVersion
    FROM fin.PaymentOrder AS o
    WHERE o.PaymentBatchId = @Id
    ORDER BY o.RequestedExecutionDate, o.IsUrgent DESC, o.Id;
END;
