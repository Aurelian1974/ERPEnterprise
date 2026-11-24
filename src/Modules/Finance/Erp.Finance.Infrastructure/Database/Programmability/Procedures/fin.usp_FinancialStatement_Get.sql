CREATE OR ALTER PROCEDURE fin.usp_FinancialStatement_Get
    @CompanyId  int,
    @Year       smallint,
    @Kind       tinyint
AS
BEGIN
    -- Situatiile unui an (macheta c5-04): antetul si liniile formularelor, fara continutul fisierului.
    SET NOCOUNT ON;
    DECLARE @Id int = (SELECT Id FROM fin.FinancialStatement WHERE CompanyId = @CompanyId AND Year = @Year AND Kind = @Kind);
    SELECT Id, Year, Kind, EntityCategory, Status, IsBalanced, CalculatedAt, GeneratedAt, SubmittedAt, RegistrationNumber, FileName, RowVersion
    FROM fin.FinancialStatement WHERE Id = @Id;
    SELECT FormCode, RowCode, Label, IsTotal, CurrentYear, PriorYear FROM fin.FinancialStatementLine WHERE StatementId = @Id ORDER BY FormCode, SortOrder;
END;
