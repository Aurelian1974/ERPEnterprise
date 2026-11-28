CREATE OR ALTER PROCEDURE inv.usp_StockMovement_Search
    @CompanyId     int,
    @WarehouseId   int = NULL,
    @ItemVariantId int = NULL,
    @DateFrom      date = NULL,
    @DateTo        date = NULL,
    @AfterSeq      bigint = NULL,
    @Top           int = 200
AS
BEGIN
    -- Registrul de miscari (02.05, S2 10; macheta s2-05), in ordinea totala (data, PostingSeq), paginat keyset dupa PostingSeq (SP-10). Soldul
    -- dupa fiecare miscare se calculeaza doar cand filtrul fixeaza cheia (gestiunea si varianta), din toate miscarile cheii.
    SET NOCOUNT ON;

    WITH Movements AS (
        SELECT m.Id, m.PostingSeq, m.MovementDate, m.WarehouseId, m.DepotId, m.ItemVariantId, m.MovementTypeId, m.QtyBase, m.UnitCost, m.CostAmount,
            m.SourceModule, m.SourceDocumentType, m.SourceDocumentId, m.SourceDocumentNumber, m.ReasonId, m.IsForcedNegative, m.ReversalOfId,
            CASE WHEN @WarehouseId IS NOT NULL AND @ItemVariantId IS NOT NULL
                 THEN SUM(m.QtyBase) OVER (PARTITION BY m.DepotId ORDER BY m.MovementDate, m.PostingSeq ROWS UNBOUNDED PRECEDING) END AS BalanceQty
        FROM inv.StockMovement AS m
        WHERE m.CompanyId = @CompanyId AND (@WarehouseId IS NULL OR m.WarehouseId = @WarehouseId)
          AND (@ItemVariantId IS NULL OR m.ItemVariantId = @ItemVariantId) AND (@DateTo IS NULL OR m.MovementDate <= @DateTo)
    )
    SELECT TOP (@Top) x.Id, x.PostingSeq, x.MovementDate, x.WarehouseId, w.Code AS WarehouseCode, w.DimensionValueId AS WarehouseDimensionId, x.DepotId,
        x.ItemVariantId, v.Code AS VariantCode, t.Code AS MovementTypeCode, x.QtyBase, x.UnitCost, x.CostAmount, x.BalanceQty, x.SourceModule,
        x.SourceDocumentType, x.SourceDocumentId, x.SourceDocumentNumber, r.Code AS ReasonCode, x.IsForcedNegative, x.ReversalOfId
    FROM Movements AS x
    JOIN inv.Warehouse AS w ON w.Id = x.WarehouseId
    JOIN core.ItemVariant AS v ON v.Id = x.ItemVariantId
    JOIN inv.MovementType AS t ON t.Id = x.MovementTypeId
    LEFT JOIN inv.MovementReason AS r ON r.Id = x.ReasonId
    WHERE (@DateFrom IS NULL OR x.MovementDate >= @DateFrom) AND (@AfterSeq IS NULL OR x.PostingSeq > @AfterSeq)
    ORDER BY x.PostingSeq;
END;
