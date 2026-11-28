CREATE OR ALTER PROCEDURE inv.usp_LotChange_SetStatus
    @CompanyId  int,
    @Id         int,
    @Status     tinyint,
    @Number     nvarchar(40) = NULL,
    @RowVersion binary(8),
    @Check      bit = 0,
    @UserId     int
AS
BEGIN
    -- Starea documentului SL: verificarea inainte de validare (@Check = 1: blocat, ciorna, versiunea vazuta), validarea (2, cu numarul) sau
    -- anularea ciornei (4).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Current tinyint, @Version binary(8);
    SELECT @Current = Status, @Version = RowVersion FROM inv.LotChange WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Current IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Check = 1 OR @Status = 4
    BEGIN
        IF @Version <> @RowVersion
            THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Current <> 1
            THROW 54118, N'Documentul nu mai este ciornă.', 1;
    END;

    IF @Check = 0
        UPDATE inv.LotChange
        SET Status = @Status, Number = COALESCE(@Number, Number), ValidatedAt = CASE WHEN @Status = 2 THEN SYSUTCDATETIME() END,
            ValidatedBy = CASE WHEN @Status = 2 THEN @UserId END, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;

    -- La validare: lotul de destinatie creat de registru se noteaza pe linie, iar lotul nou se leaga de lotul sursa (genealogia, S4 3.6).
    IF @Check = 0 AND @Status = 2
    BEGIN
        DECLARE @In TABLE (LineId bigint PRIMARY KEY, MovementId bigint, LotId int);
        INSERT INTO @In (LineId, MovementId, LotId)
        SELECT m.SourceLineId, MIN(m.Id), MIN(m.LotId)
        FROM inv.StockMovement AS m
        WHERE m.CompanyId = @CompanyId AND m.SourceModule = 'inv' AND m.SourceDocumentType = 'SL' AND m.SourceDocumentId = @Id AND m.SourceRole = 2
          AND m.ReversalOfId IS NULL
        GROUP BY m.SourceLineId;

        INSERT INTO inv.LotGenealogy (ParentLotId, ChildLotId, SourceMovementId, QtyBase)
        SELECT l.FromLotId, i.LotId, i.MovementId, l.QtyBase
        FROM inv.LotChangeLine AS l JOIN @In AS i ON i.LineId = l.Id
        WHERE l.LotChangeId = @Id AND l.ToLotId IS NULL AND i.LotId IS NOT NULL AND i.LotId <> l.FromLotId;

        UPDATE l SET ToLotId = i.LotId
        FROM inv.LotChangeLine AS l JOIN @In AS i ON i.LineId = l.Id
        WHERE l.LotChangeId = @Id AND l.ToLotId IS NULL;
    END;

    SELECT Id, RowVersion FROM inv.LotChange WHERE Id = @Id;
END;
