CREATE OR ALTER PROCEDURE fin.usp_ExpenseReport_SetSettlement
    @Id             int,
    @CashDocumentId int
AS
BEGIN
    -- Dispozitia (ciorna) generata din decont pentru diferenta (F1 3.7 regula 4).
    SET NOCOUNT ON;
    UPDATE fin.ExpenseReport SET SettlementCashDocumentId = @CashDocumentId WHERE Id = @Id;
END;
