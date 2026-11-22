CREATE OR ALTER PROCEDURE fin.usp_MatchingRule_List
    @CompanyId int
AS
BEGIN
    -- Regulile de potrivire ale firmei (02.02, F2 3.5; macheta cf-04), in ordinea aplicarii.
    SET NOCOUNT ON;

    SELECT r.Id, r.Priority, r.Name, r.TreasuryAccountId, r.Direction, r.CounterpartyIban, r.CounterpartyFiscalCode, r.RemittanceContains, r.BankTransactionCode,
        r.AmountFrom, r.AmountTo, r.TreasuryOperationTypeId AS OperationTypeId, t.Code AS OperationTypeCode, r.PartnerId, p.Name AS PartnerName, r.CostCenterId,
        r.ProjectId, r.NoteTemplate, r.AutoValidate, r.CreatedFromLineId, r.IsActive, r.RowVersion
    FROM fin.MatchingRule AS r
    JOIN fin.TreasuryOperationType AS t ON t.Id = r.TreasuryOperationTypeId
    LEFT JOIN core.Partner AS p ON p.Id = r.PartnerId
    WHERE r.CompanyId = @CompanyId
    ORDER BY r.IsActive DESC, r.Priority, r.Id;
END;
