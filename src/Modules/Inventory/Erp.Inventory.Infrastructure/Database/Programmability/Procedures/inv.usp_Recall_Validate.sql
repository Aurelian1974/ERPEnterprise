CREATE OR ALTER PROCEDURE inv.usp_Recall_Validate
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8),
    @Number     nvarchar(40),
    @UserId     int
AS
BEGIN
    -- Validarea retragerii (02.05, S4 3.6 pasii 2 si 4): documentul blocat, ciorna; instantaneul clientilor care au primit loturile (vanzarile
    -- nestornate, pe client si document). Trecerea loturilor in Retras o face apelantul (inv.usp_Lot_ChangeStatus, cu istoric si eveniment).
    -- Intoarce loturile de retras.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8);
    SELECT @Status = Status, @Version = RowVersion FROM inv.Recall WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 54118, N'Documentul nu mai este ciornă.', 1;

    INSERT INTO inv.RecallCustomer (RecallId, LotId, PartnerId, SourceDocumentType, SourceDocumentId, SourceDocumentNumber, MovementDate, QtyBase)
    SELECT @Id, m.LotId, m.PartnerId, m.SourceDocumentType, m.SourceDocumentId, MAX(m.SourceDocumentNumber), MIN(m.MovementDate), -SUM(m.QtyBase)
    FROM inv.StockMovement AS m
    JOIN inv.MovementType AS t ON t.Id = m.MovementTypeId
    WHERE m.CompanyId = @CompanyId AND t.Code IN ('ISSUE_SALE', 'POS_SALE', 'RETURN_FROM_CUSTOMER')
      AND m.LotId IN (SELECT LotId FROM inv.RecallLot WHERE RecallId = @Id)
    GROUP BY m.LotId, m.PartnerId, m.SourceDocumentType, m.SourceDocumentId
    HAVING SUM(m.QtyBase) < 0;

    UPDATE inv.Recall SET Status = 2, Number = @Number, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;

    SELECT Id, RowVersion FROM inv.Recall WHERE Id = @Id;
    SELECT l.LotId, x.Status, x.SupplierPartnerId FROM inv.RecallLot AS l JOIN inv.Lot AS x ON x.Id = l.LotId WHERE l.RecallId = @Id;
    SELECT COUNT(DISTINCT PartnerId) AS Customers FROM inv.RecallCustomer WHERE RecallId = @Id;
END;
