CREATE OR ALTER PROCEDURE inv.usp_StockCost_History
    @CompanyId     int,
    @WarehouseId   int,
    @DepotId       int = NULL,
    @ItemVariantId int,
    @LotId         int = NULL
AS
BEGIN
    -- Istoria unei chei pentru recalculare (02.05, S3 3.5): randul de sold blocat, metoda si costul de rezerva, toate miscarile in ordinea totala
    -- (data, PostingSeq) si, pentru intrarile din transferuri, consumurile si valoarea iesirii pereche (din cheia sursei).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Locked bigint = (SELECT Id FROM inv.Stock WITH (UPDLOCK, ROWLOCK)
                              WHERE WarehouseId = @WarehouseId AND DepotKey = ISNULL(@DepotId, 0) AND ItemVariantId = @ItemVariantId
                                AND LotKey = ISNULL(@LotId, 0));

    SELECT CAST(CASE WHEN w.ValuationBasis = 2 THEN 2 ELSE COALESCE(iss.ValuationMethod, ss.DefaultValuationMethod, 2) END AS tinyint) AS Method,
        iss.StandardCost AS FallbackUnitCost
    FROM inv.Warehouse AS w
    JOIN core.ItemVariant AS v ON v.Id = @ItemVariantId
    LEFT JOIN inv.ItemStockSettings AS iss ON iss.CompanyId = @CompanyId AND iss.ItemId = v.ItemId
    LEFT JOIN inv.StockSettings AS ss ON ss.CompanyId = @CompanyId
    WHERE w.Id = @WarehouseId AND w.CompanyId = @CompanyId;

    SELECT m.Id, m.MovementDate, m.AccountingDate, m.PostingSeq, m.QtyBase, m.UnitCost, m.CostAmount, m.CounterMovementId, m.ReversalOfId, m.SourceModule,
        m.SourceDocumentType, m.SourceDocumentId
    FROM inv.StockMovement AS m
    WHERE m.WarehouseId = @WarehouseId AND ISNULL(m.DepotId, 0) = ISNULL(@DepotId, 0) AND m.ItemVariantId = @ItemVariantId
      AND ISNULL(m.LotId, 0) = ISNULL(@LotId, 0)
    ORDER BY m.MovementDate, m.PostingSeq;

    SELECT o.Id AS MovementId, o.CostAmount FROM inv.StockMovement AS o
    WHERE o.Id IN (SELECT m.CounterMovementId FROM inv.StockMovement AS m
                   WHERE m.WarehouseId = @WarehouseId AND ISNULL(m.DepotId, 0) = ISNULL(@DepotId, 0) AND m.ItemVariantId = @ItemVariantId
                     AND ISNULL(m.LotId, 0) = ISNULL(@LotId, 0) AND m.CounterMovementId IS NOT NULL);

    SELECT x.OutboundMovementId, x.Qty, x.CostAmount, c.LayerDate, c.PostingSeq, c.Id AS LayerId
    FROM inv.CostLayerConsumption AS x
    JOIN inv.CostLayer AS c ON c.Id = x.CostLayerId
    WHERE x.OutboundMovementId IN (SELECT m.CounterMovementId FROM inv.StockMovement AS m
                                   WHERE m.WarehouseId = @WarehouseId AND ISNULL(m.DepotId, 0) = ISNULL(@DepotId, 0) AND m.ItemVariantId = @ItemVariantId
                                     AND ISNULL(m.LotId, 0) = ISNULL(@LotId, 0) AND m.CounterMovementId IS NOT NULL)
    ORDER BY x.OutboundMovementId, x.Id;
END;
