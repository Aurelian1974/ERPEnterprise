CREATE OR ALTER PROCEDURE fin.usp_FinancialStatement_GetFile
    @CompanyId  int,
    @Year       smallint,
    @Kind       tinyint
AS
BEGIN
    -- Fisierul generat al situatiilor financiare, pentru descarcare.
    SET NOCOUNT ON;
    SELECT FileName, FileContent FROM fin.FinancialStatement WHERE CompanyId = @CompanyId AND Year = @Year AND Kind = @Kind AND FileContent IS NOT NULL;
END;
