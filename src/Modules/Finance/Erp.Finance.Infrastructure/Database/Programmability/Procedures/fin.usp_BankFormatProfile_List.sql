CREATE OR ALTER PROCEDURE fin.usp_BankFormatProfile_List
    @CompanyId int
AS
BEGIN
    -- Profilele de format bancar (02.02, F2 3.1; macheta cf-04), cu numarul conturilor firmei curente care le folosesc.
    SET NOCOUNT ON;

    SELECT p.Id, p.Code, p.Name, p.BankBic, p.StatementFormat, p.CsvMapping, p.PaymentFormat, p.PaymentMapping, p.MaxRemittanceLength, p.IsSystem,
        p.IsActive, (SELECT COUNT(*) FROM fin.TreasuryAccount AS a WHERE a.BankFormatProfileId = p.Id AND a.CompanyId = @CompanyId) AS AccountCount,
        p.RowVersion
    FROM fin.BankFormatProfile AS p
    ORDER BY p.IsActive DESC, p.IsSystem DESC, p.CodeKey;
END;
