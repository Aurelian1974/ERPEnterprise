CREATE OR ALTER PROCEDURE fin.usp_BankOperationType_List
AS
BEGIN
    -- Tipurile de operatiuni folosite la banca (02.02, F2 3.2), active, pentru editoarele Bancii (parti, reguli, OP-uri).
    SET NOCOUNT ON;
    SELECT Id, Code, Name, Direction, RequiresPartner, RequiresEmployee, RequiresCounterAccount
    FROM fin.TreasuryOperationType WHERE IsActive = 1 AND AppliesToBank = 1 ORDER BY Direction, Name;
END;
