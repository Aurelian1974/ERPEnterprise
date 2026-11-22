CREATE OR ALTER PROCEDURE fin.usp_CashAdvance_MarkOverdue
    @Today date
AS
BEGIN
    -- Jobul zilnic al avansurilor (F1 3.5 si 4.4 pasul 6): avansurile acordate sau decontate partial cu termenul trecut devin Depasit.
    -- Intoarce avansurile marcate (pentru jurnal si, cu fluxul 4.7 din Nucleu, notificari).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE fin.CashAdvance
    SET Status = 4, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
    OUTPUT inserted.Id, inserted.CompanyId, inserted.EmployeeId, inserted.Balance, inserted.SettlementDueDate
    WHERE Status IN (1, 2) AND SettlementDueDate < @Today AND Balance > 0;
END;
