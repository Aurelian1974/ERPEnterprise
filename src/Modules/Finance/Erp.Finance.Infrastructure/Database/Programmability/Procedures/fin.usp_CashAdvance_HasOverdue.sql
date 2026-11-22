CREATE OR ALTER PROCEDURE fin.usp_CashAdvance_HasOverdue
    @CompanyId  int,
    @EmployeeId int,
    @Today      date
AS
BEGIN
    -- Angajatul are un avans nedecontat dupa termen (F1 2 regula 5): marcat Depasit sau acordat / decontat partial cu termenul trecut.
    SET NOCOUNT ON;

    SELECT CAST(CASE WHEN EXISTS (
        SELECT 1 FROM fin.CashAdvance
        WHERE CompanyId = @CompanyId AND EmployeeId = @EmployeeId
          AND (Status = 4 OR (Status IN (1, 2) AND SettlementDueDate < @Today))) THEN 1 ELSE 0 END AS bit);
END;
