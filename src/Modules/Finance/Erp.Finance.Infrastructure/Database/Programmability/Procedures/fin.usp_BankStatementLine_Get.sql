CREATE OR ALTER PROCEDURE fin.usp_BankStatementLine_Get
    @CompanyId int,
    @Lines     fin.BankLineVersionList READONLY
AS
BEGIN
    -- Liniile de extras cerute, ale firmei, cu extrasul si contul lor, apoi partile lor (02.02, F2 3.4): pentru explicare, validare
    -- (evenimentele) si desfacerea validarii.
    SET NOCOUNT ON;

    SELECT l.Id, l.BankStatementId, s.StatementNo, l.LineNumber, l.BookingDate, l.ValueDate, l.Amount, l.CounterpartyName, l.CounterpartyIban,
        l.RemittanceInfo, l.EndToEndId, l.BankReference, l.MatchStatus, l.PartnerId, p.Name AS PartnerName, l.TreasuryAccountId, a.Kind AS AccountKind,
        a.CurrencyCode, l.ValidatedAt, l.RowVersion
    FROM fin.BankStatementLine AS l
    JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId AND s.CompanyId = @CompanyId
    JOIN fin.TreasuryAccount AS a ON a.Id = l.TreasuryAccountId
    LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
    WHERE l.Id IN (SELECT Id FROM @Lines)
    ORDER BY l.Id;

    SELECT x.BankStatementLineId, x.PartNumber, x.TreasuryOperationTypeId AS OperationTypeId, t.Code AS OperationTypeCode, t.Name AS OperationTypeName, x.Amount,
        x.PartnerId, pa.Name AS PartnerName, x.EmployeeId, x.SettlementRef, x.PaymentOrderId, x.CashDocumentId, x.CounterTreasuryAccountId,
        x.BudgetObligationRef, x.CostCenterId, x.ProjectId, x.Note
    FROM fin.BankStatementLinePart AS x
    JOIN fin.BankStatementLine AS l ON l.Id = x.BankStatementLineId
    JOIN fin.BankStatement AS s ON s.Id = l.BankStatementId AND s.CompanyId = @CompanyId
    JOIN fin.TreasuryOperationType AS t ON t.Id = x.TreasuryOperationTypeId
    LEFT JOIN core.Partner AS pa ON pa.Id = x.PartnerId
    WHERE x.BankStatementLineId IN (SELECT Id FROM @Lines)
    ORDER BY x.BankStatementLineId, x.PartNumber;

    SELECT m.BankStatementLineId, m.Kind, m.PaymentOrderId, m.PartnerId, m.Amount, m.Score, m.Description
    FROM fin.MatchSuggestion AS m
    WHERE m.BankStatementLineId IN (SELECT Id FROM @Lines)
    ORDER BY m.BankStatementLineId, m.Score DESC, m.Id;
END;
