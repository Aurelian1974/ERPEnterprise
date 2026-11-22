CREATE OR ALTER PROCEDURE fin.usp_CashDocument_Get
    @CompanyId      int,
    @CashDocumentId int
AS
BEGIN
    -- Documentul de casa cu liniile lui (macheta f1-03) si datele necesare validarii: casa (serii, punct de lucru), tipul partenerului
    -- (plafoane PJ / PF) si, pe linii, sensul si categoria de plafon a tipului de operatiune.
    SET NOCOUNT ON;

    SELECT d.Id, d.CompanyId, d.TreasuryAccountId, a.Code AS TreasuryAccountCode, a.Name AS TreasuryAccountName, a.BranchId, a.ReceiptSeriesId,
           a.CashInSeriesId, a.CashOutSeriesId, d.Kind, d.Number, d.DocumentDate, d.Status, d.PartnerId, p.Name AS PartnerName,
           p.PartnerType, d.EmployeeId, CONCAT(e.LastName, N' ', e.FirstName) AS EmployeeName, d.PersonName,
           CAST(CASE WHEN d.PersonIdEncrypted IS NULL THEN 0 ELSE 1 END AS bit) AS HasPersonId, d.CurrencyCode, d.ExchangeRate, d.RateDate,
           d.TotalAmount, d.TotalAmountRon, d.Description, d.ReversalOfId, r.Number AS ReversalOfNumber, d.ReversedById, rb.Number AS ReversedByNumber,
           d.ValidatedAt, v.DisplayName AS ValidatedByName, d.CreatedAt, d.ModifiedAt, m.DisplayName AS ModifiedByName, d.RowVersion
    FROM fin.CashDocument AS d
    JOIN fin.TreasuryAccount AS a ON a.Id = d.TreasuryAccountId
    LEFT JOIN core.Partner AS p ON p.Id = d.PartnerId
    LEFT JOIN core.Employee AS e ON e.Id = d.EmployeeId
    LEFT JOIN fin.CashDocument AS r ON r.Id = d.ReversalOfId
    LEFT JOIN fin.CashDocument AS rb ON rb.Id = d.ReversedById
    LEFT JOIN core.[User] AS v ON v.Id = d.ValidatedBy
    LEFT JOIN core.[User] AS m ON m.Id = ISNULL(d.ModifiedBy, d.CreatedBy)
    WHERE d.Id = @CashDocumentId AND d.CompanyId = @CompanyId;

    SELECT l.Id, l.LineNumber, l.TreasuryOperationTypeId, t.Code AS OperationTypeCode, t.Name AS OperationTypeName, t.Direction,
           t.CashLimitCategory, l.Amount, l.AmountRon, l.CounterTreasuryAccountId, ca.Code AS CounterTreasuryAccountCode, l.SettlementRef,
           l.CostCenterId, l.ProjectId, l.Note
    FROM fin.CashDocumentLine AS l
    JOIN fin.CashDocument AS d ON d.Id = l.CashDocumentId
    JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
    LEFT JOIN fin.TreasuryAccount AS ca ON ca.Id = l.CounterTreasuryAccountId
    WHERE l.CashDocumentId = @CashDocumentId AND d.CompanyId = @CompanyId
    ORDER BY l.LineNumber;
END;
