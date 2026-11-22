CREATE OR ALTER PROCEDURE fin.usp_ExpenseType_List
AS
BEGIN
    -- Configurare › Finante › Trezorerie › Cheltuieli si diurna (02.02, F1 3.7; macheta cf-02): toate versiunile, pe cod si data de vigoare.
    SET NOCOUNT ON;

    SELECT Id, Code, Name, IsPerDiem, DailyAmount, NonTaxableDailyLimit, CurrencyCode, CountryCode, VatDeductibleOnReceipt, ValidFrom, ValidTo,
           CounterGlAccountCode, IsSystem, IsActive, RowVersion
    FROM fin.ExpenseType
    ORDER BY IsActive DESC, IsPerDiem, CodeKey, ValidFrom DESC;
END;
