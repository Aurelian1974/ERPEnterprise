CREATE OR ALTER PROCEDURE fin.usp_CashAdvance_RefreshStatus
    @CashAdvanceId int,
    @UserId        int
AS
BEGIN
    -- Starea unui avans acordat dupa decont (F1 3.5): cu decontul validat, Decontat la sold zero si Decontat partial altfel. Avansurile
    -- de platit si cele stornate nu se schimba aici; termenul depasit il marcheaza jobul zilnic (fin.usp_CashAdvance_MarkOverdue).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    UPDATE a
    SET Status = CASE WHEN a.Balance = 0 THEN 3 ELSE 2 END, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.CashAdvance AS a
    WHERE a.Id = @CashAdvanceId AND a.Status IN (1, 2, 3, 4)
      AND EXISTS (SELECT 1 FROM fin.ExpenseReport AS r WHERE r.CashAdvanceId = a.Id AND r.Status = 4);
END;
