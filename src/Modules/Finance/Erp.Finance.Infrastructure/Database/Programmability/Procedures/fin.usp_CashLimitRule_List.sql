CREATE OR ALTER PROCEDURE fin.usp_CashLimitRule_List
    @CompanyId int
AS
BEGIN
    -- Configurare › Finante › Trezorerie › Plafoane de numerar (02.02, F1 3.6; macheta cf-03): plafoanele legale livrate (doar citire)
    -- si plafoanele proprii ale firmei curente, cu data de vigoare.
    SET NOCOUNT ON;

    SELECT Id, CompanyId, Category, CurrencyCode, Amount, Action, ValidFrom, ValidTo, LegalReference, CAST(CASE WHEN CompanyId IS NULL THEN 1 ELSE 0 END AS bit) AS IsLegal,
           RowVersion
    FROM fin.CashLimitRule
    WHERE CompanyId IS NULL OR CompanyId = @CompanyId
    ORDER BY CASE WHEN CompanyId IS NULL THEN 0 ELSE 1 END, Category, ValidFrom DESC;
END;
