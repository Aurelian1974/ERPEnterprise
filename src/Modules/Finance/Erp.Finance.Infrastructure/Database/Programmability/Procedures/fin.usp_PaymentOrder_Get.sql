CREATE OR ALTER PROCEDURE fin.usp_PaymentOrder_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Un ordin de plata cu liniile lui (02.02, F2 3.6): antetul (cu partenerul, inactiv la ANAF, si filiala contului pentru serie), apoi liniile.
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM fin.PaymentOrder WHERE Id = @Id AND CompanyId = @CompanyId)
        RETURN;

    SELECT o.Id, o.TreasuryAccountId, a.Code AS AccountCode, a.BranchId, o.Number, o.DocumentDate, o.RequestedExecutionDate, o.IsUrgent, o.Origin, o.Purpose,
        o.PartnerId, p.Name AS PartnerName, CAST(ISNULL(p.AnafInactive, 0) AS bit) AS PartnerAnafInactive, o.EmployeeId, o.PartnerBankAccountId,
        o.BeneficiaryName, o.BeneficiaryIban, o.BeneficiaryBic, o.BeneficiaryFiscalCode, o.IsFreeIban, o.CurrencyCode, o.Amount, o.RemittanceInfo,
        o.EndToEndId, o.BudgetObligationRef, o.Status, o.ConfirmationStatus, o.BankStatementLineId, o.PaymentBatchId, o.ValidatedAt, vu.DisplayName AS ValidatedByName,
        o.ValidatedBy, o.ApprovedAt, au.DisplayName AS ApprovedByName, o.ReversalOfId,
        (SELECT TOP (1) r.Id FROM fin.PaymentOrder AS r WHERE r.ReversalOfId = o.Id) AS ReversedById, o.RowVersion
    FROM fin.PaymentOrder AS o
    JOIN fin.TreasuryAccount AS a ON a.Id = o.TreasuryAccountId
    LEFT JOIN core.Partner AS p ON p.Id = o.PartnerId
    LEFT JOIN core.[User] AS vu ON vu.Id = o.ValidatedBy
    LEFT JOIN core.[User] AS au ON au.Id = o.ApprovedBy
    WHERE o.Id = @Id;

    SELECT l.Id, l.LineNumber, l.TreasuryOperationTypeId AS OperationTypeId, t.Code AS OperationTypeCode, t.Name AS OperationTypeName, l.SettlementRef,
        l.Amount, l.CostCenterId, l.ProjectId
    FROM fin.PaymentOrderLine AS l
    JOIN fin.TreasuryOperationType AS t ON t.Id = l.TreasuryOperationTypeId
    WHERE l.PaymentOrderId = @Id
    ORDER BY l.LineNumber;
END;
