CREATE OR ALTER PROCEDURE inv.usp_StockMovement_ForReversal
    @CompanyId        int,
    @SourceModule     varchar(20),
    @SourceDocumentId bigint,
    @SourceLineIds    inv.BigIdList READONLY
AS
BEGIN
    -- Miscarile de stornat ale unui document sursa (02.05, S2 4.6): toate sau doar liniile date (@SourceLineIds gol = toate), nestornate.
    -- Nimic de stornat sau miscare deja stornata: 54114. Stornarea o costeaza motorul (valoarea exacta a originalului) si o scrie
    -- inv.usp_StockMovement_Post.
    SET NOCOUNT ON;

    DECLARE @Rows TABLE (MovementId bigint PRIMARY KEY);
    INSERT INTO @Rows
    SELECT m.Id FROM inv.StockMovement AS m
    WHERE m.CompanyId = @CompanyId AND m.SourceModule = @SourceModule AND m.SourceDocumentId = @SourceDocumentId AND m.ReversalOfId IS NULL
      AND (NOT EXISTS (SELECT 1 FROM @SourceLineIds) OR m.SourceLineId IN (SELECT Id FROM @SourceLineIds));
    IF NOT EXISTS (SELECT 1 FROM @Rows)
        THROW 54114, N'Documentul nu are mișcări de stornat.', 1;
    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN inv.StockMovement AS x ON x.ReversalOfId = r.MovementId)
        THROW 54114, N'Mișcarea a fost deja stornată.', 1;

    SELECT m.Id, m.SourceLineId, m.SourceRole, t.Code AS MovementTypeCode, m.DepotId, m.WarehouseId, m.ItemVariantId, m.QtyBase, m.LotId, m.SerialId,
        m.CostCenterId, m.ProjectId, m.ReasonId, m.CostAmount, m.CounterMovementId
    FROM inv.StockMovement AS m
    JOIN @Rows AS r ON r.MovementId = m.Id
    JOIN inv.MovementType AS t ON t.Id = m.MovementTypeId
    ORDER BY m.PostingSeq DESC;
END;
