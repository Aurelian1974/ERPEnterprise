CREATE OR ALTER PROCEDURE fin.usp_BankFolderImport_Accounts
AS
BEGIN
    -- Conturile cu import automat din folder (02.02, F2 3.1 si 4.1), din toate firmele: active, de tip Banca, Card sau Depozit.
    SET NOCOUNT ON;
    SELECT Id, CompanyId, Code, ImportFolder FROM fin.TreasuryAccount
    WHERE ImportFolder IS NOT NULL AND IsActive = 1 AND Kind IN (1, 3, 5)
    ORDER BY CompanyId, Code;
END;
