CREATE OR ALTER PROCEDURE fin.usp_CashFlowPlanItem_Delete
    @CompanyId  int,
    @Id         int
AS
BEGIN
    -- Stergerea unei sume planificate (F5 3.5); nu e referita din alte tabele (instantaneele pastreaza doar sumele pe categorii).
    SET NOCOUNT ON;

    DELETE FROM fin.CashFlowPlanItem WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @@ROWCOUNT = 0
        THROW 51712, N'Suma planificată nu există.', 1;
END;
