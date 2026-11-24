CREATE OR ALTER PROCEDURE fin.usp_TaxReturn_Get
    @CompanyId  int,
    @Id         int
AS
BEGIN
    -- O declaratie (machetele c4-01, c4-02): antetul, randurile si fisierele (fara continut).
    SET NOCOUNT ON;

    SELECT r.Id, r.FormCode, f.Name AS FormName, r.PeriodFrom, r.PeriodTo, r.Kind, r.Status, r.NeedsReview, r.DueDate, r.CalculatedAt, r.CalculationHash, r.GeneratedAt,
        r.SubmittedAt, r.RegistrationNumber, r.AmountDue, r.AmountRefundable, r.RowVersion
    FROM fin.TaxReturn AS r JOIN fin.TaxForm AS f ON f.Code = r.FormCode WHERE r.Id = @Id AND r.CompanyId = @CompanyId;

    SELECT RowCode, ColumnCode, Label, IsManual, CalculatedValue, ManualValue, ManualReason
    FROM fin.TaxReturnLine WHERE ReturnId = @Id ORDER BY SortOrder, RowCode, ColumnCode;

    SELECT Id, Kind, FileName, DATALENGTH(Content) AS Size, CreatedAt FROM fin.TaxReturnFile WHERE ReturnId = @Id ORDER BY Id;
END;
