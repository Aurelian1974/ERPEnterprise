CREATE OR ALTER PROCEDURE inv.usp_Recall_Close
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Inchiderea retragerii (02.05, S4 3.6 pasul 6): stocul loturilor e 0 in toate gestiunile si fiecare client are rezultatul notat (54466).
    -- Inchiderea nu schimba starea loturilor.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8);
    SELECT @Status = Status, @Version = RowVersion FROM inv.Recall WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 2
        THROW 54118, N'Retragerea nu este deschisă.', 1;
    IF EXISTS (SELECT 1 FROM inv.Stock AS s WHERE s.QtyOnHand <> 0 AND s.LotId IN (SELECT LotId FROM inv.RecallLot WHERE RecallId = @Id))
       OR EXISTS (SELECT 1 FROM inv.RecallCustomer WHERE RecallId = @Id AND Outcome IS NULL)
        THROW 54466, N'Retragerea se închide când stocul loturilor e zero și fiecare client are rezultatul notat.', 1;

    UPDATE inv.Recall SET Status = 3, ClosedAt = SYSUTCDATETIME(), ClosedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id;
    SELECT Id, RowVersion FROM inv.Recall WHERE Id = @Id;
END;
