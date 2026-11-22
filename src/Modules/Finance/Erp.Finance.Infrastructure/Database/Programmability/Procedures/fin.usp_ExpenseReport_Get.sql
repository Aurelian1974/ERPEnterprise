CREATE OR ALTER PROCEDURE fin.usp_ExpenseReport_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Decontul de cheltuieli (macheta f1-06): antetul cu avansul, aprobarea si dispozitia diferentei; liniile cu tipul de cheltuiala.
    SET NOCOUNT ON;

    SELECT r.Id, r.EmployeeId, CONCAT(e.LastName, N' ', e.FirstName) AS EmployeeName, r.CashAdvanceId, a.Amount AS AdvanceGranted, a.Purpose AS AdvancePurpose,
           r.Number, r.DocumentDate, r.CurrencyCode, r.TotalJustified, r.AdvanceAmount, r.Difference, r.Description, r.Status, r.CreatedBy,
           c.DisplayName AS CreatedByName, r.CreatedAt, r.SubmittedAt, s.DisplayName AS SubmittedByName, r.ApprovedAt, ap.DisplayName AS ApprovedByName,
           r.ValidatedAt, r.SettlementCashDocumentId, sd.Number AS SettlementNumber, sd.Status AS SettlementStatus, r.RowVersion
    FROM fin.ExpenseReport AS r
    JOIN core.Employee AS e ON e.Id = r.EmployeeId
    LEFT JOIN fin.CashAdvance AS a ON a.Id = r.CashAdvanceId
    LEFT JOIN core.[User] AS c ON c.Id = r.CreatedBy
    LEFT JOIN core.[User] AS s ON s.Id = r.SubmittedBy
    LEFT JOIN core.[User] AS ap ON ap.Id = r.ApprovedBy
    LEFT JOIN fin.CashDocument AS sd ON sd.Id = r.SettlementCashDocumentId
    WHERE r.Id = @Id AND r.CompanyId = @CompanyId;

    SELECT l.Id, l.LineNumber, l.ExpenseTypeId, t.Code AS ExpenseTypeCode, t.Name AS ExpenseTypeName, t.IsPerDiem, l.JustificationKind, l.DocumentNo,
           l.DocumentDate, l.SupplierFiscalCode, l.SupplierInvoiceId, l.Amount, l.VatAmount, l.CostCenterId, l.ProjectId, l.Days, l.DailyAmount,
           l.NonTaxableAmount, l.TaxableAmount, l.Note
    FROM fin.ExpenseReportLine AS l
    JOIN fin.ExpenseReport AS r ON r.Id = l.ExpenseReportId
    JOIN fin.ExpenseType AS t ON t.Id = l.ExpenseTypeId
    WHERE l.ExpenseReportId = @Id AND r.CompanyId = @CompanyId
    ORDER BY l.LineNumber;
END;
