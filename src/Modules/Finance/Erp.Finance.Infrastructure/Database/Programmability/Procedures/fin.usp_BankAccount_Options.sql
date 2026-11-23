CREATE OR ALTER PROCEDURE fin.usp_BankAccount_Options
    @CompanyId int
AS
BEGIN
    -- Conturile bancare active ale firmei (Banca, Card, Depozit), pentru ecranele Bancii (02.02, F2), cu dreptul Fin.Bank.View.
    SET NOCOUNT ON;
    SELECT Id, Code, Name, Kind, CurrencyCode, BankFormatProfileId, ImportFolder FROM fin.TreasuryAccount WHERE CompanyId = @CompanyId AND IsActive = 1 AND Kind IN (1, 3, 5) ORDER BY Kind, Code;
END;
