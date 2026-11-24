CREATE OR ALTER PROCEDURE fin.usp_CashFlowAlertRule_MarkAlerted
    @CompanyId  int,
    @Id         int,
    @Today      date
AS
BEGIN
    -- Marcheaza alerta zilei pe regula (F5-A12): 1 daca regula nu a mai alertat azi (si acum e marcata), 0 altfel. Atomic, deci doua
    -- rulari simultane ale jobului nu trimit de doua ori.
    SET NOCOUNT ON;

    UPDATE fin.CashFlowAlertRule SET LastAlertedOn = @Today
    WHERE Id = @Id AND CompanyId = @CompanyId AND IsActive = 1 AND (LastAlertedOn IS NULL OR LastAlertedOn < @Today);
    SELECT @@ROWCOUNT AS Marked;
END;
