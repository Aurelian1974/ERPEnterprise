CREATE OR ALTER PROCEDURE fin.usp_TreasuryOperationType_List
AS
BEGIN
    -- Configurare › Finante › Trezorerie › Tipuri de operatiuni (02.02, F1 3.2; macheta cf-01): toate tipurile, livrate si ale clientului.
    SET NOCOUNT ON;

    SELECT Id, Code, Name, Direction, RequiresPartner, RequiresEmployee, RequiresCounterAccount, SettlementKind, CounterGlAccountCode,
           CashLimitCategory, AppliesToCash, AppliesToBank, IsSystem, IsActive, RowVersion
    FROM fin.TreasuryOperationType
    ORDER BY IsActive DESC, Direction, CodeKey;
END;
