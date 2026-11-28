CREATE OR ALTER PROCEDURE inv.usp_StockCost_Rebuild
    @CompanyId     int,
    @WarehouseId   int,
    @DepotId       int = NULL,
    @ItemVariantId int,
    @LotId         int = NULL,
    @Updates       inv.tt_MovementCostUpdate READONLY,
    @Layers        inv.tt_CostLayerWrite READONLY,
    @Consumptions  inv.tt_CostConsumptionWrite READONLY,
    @Adjustments   inv.tt_CostAdjustmentWrite READONLY,
    @Qty           decimal(19,6),
    @Value         decimal(19,2),
    @LastUnitCost  decimal(19,6) = NULL,
    @UserId        int,
    @AdjustmentDocumentType varchar(30) = NULL,
    @AdjustmentDocumentId   bigint = NULL,
    @AdjustmentDate         date = NULL
AS
BEGIN
    -- Refacerea costului unei chei (02.05, S3 3.5), din calculul motorului pe toata istoria ei: straturile, consumurile si regularizarile de negativ
    -- ale cheii se inlocuiesc; miscarile al caror cost se schimba se actualizeaza (singura exceptie a triggerului, prin SESSION_CONTEXT) si primesc
    -- ajustarea de recalculare; soldul se aliniaza; cheia iese din coada. Intoarce cheile intrarilor pereche ale iesirilor schimbate (transferurile),
    -- de recalculat la randul lor. Cu @AdjustmentDocumentType (documentul CA, S3 3.3), urmele schimbarilor poarta documentul si data lui.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @KeyMovements TABLE (Id bigint PRIMARY KEY);
    INSERT INTO @KeyMovements
    SELECT Id FROM inv.StockMovement
    WHERE WarehouseId = @WarehouseId AND ISNULL(DepotId, 0) = ISNULL(@DepotId, 0) AND ItemVariantId = @ItemVariantId AND ISNULL(LotId, 0) = ISNULL(@LotId, 0);

    DELETE x FROM inv.CostLayerConsumption AS x WHERE x.OutboundMovementId IN (SELECT Id FROM @KeyMovements);
    DELETE x FROM inv.CostLayerConsumption AS x
    WHERE x.CostLayerId IN (SELECT Id FROM inv.CostLayer WHERE WarehouseId = @WarehouseId AND DepotKey = ISNULL(@DepotId, 0)
                            AND ItemVariantId = @ItemVariantId AND LotKey = ISNULL(@LotId, 0));
    DELETE FROM inv.CostLayer WHERE WarehouseId = @WarehouseId AND DepotKey = ISNULL(@DepotId, 0) AND ItemVariantId = @ItemVariantId AND LotKey = ISNULL(@LotId, 0);
    DELETE FROM inv.CostAdjustment
    WHERE WarehouseId = @WarehouseId AND ISNULL(DepotId, 0) = ISNULL(@DepotId, 0) AND ItemVariantId = @ItemVariantId AND ISNULL(LotId, 0) = ISNULL(@LotId, 0)
      AND Kind = 2;

    EXEC sys.sp_set_session_context N'inv.CostRecalc', 1;
    UPDATE m SET UnitCost = u.UnitCost, CostAmount = u.CostAmount
    FROM inv.StockMovement AS m JOIN @Updates AS u ON u.MovementId = m.Id;
    EXEC sys.sp_set_session_context N'inv.CostRecalc', NULL;

    INSERT INTO inv.CostAdjustment (CompanyId, WarehouseId, DepotId, ItemVariantId, LotId, MovementId, Kind, InStock, Amount, AccountingDate,
        SourceDocumentType, SourceDocumentId, CreatedBy)
    SELECT @CompanyId, a.WarehouseId, a.DepotId, a.ItemVariantId, a.LotId, a.MovementId, a.Kind, a.InStock, a.Amount,
        CASE WHEN a.Kind = 2 OR @AdjustmentDocumentType IS NULL THEN m.AccountingDate ELSE @AdjustmentDate END,
        CASE WHEN a.Kind = 2 OR @AdjustmentDocumentType IS NULL THEN m.SourceDocumentType ELSE @AdjustmentDocumentType END,
        CASE WHEN a.Kind = 2 OR @AdjustmentDocumentType IS NULL THEN m.SourceDocumentId ELSE @AdjustmentDocumentId END, @UserId
    FROM @Adjustments AS a
    JOIN inv.StockMovement AS m ON m.Id = a.MovementId;

    DECLARE @LayerMap TABLE (LayerRef int PRIMARY KEY, LayerId bigint);
    MERGE inv.CostLayer AS target
    USING (SELECT l.*, m.PostingSeq AS MovementSeq FROM @Layers AS l JOIN inv.StockMovement AS m ON m.Id = l.MovementId) AS source
    ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (CompanyId, WarehouseId, DepotId, ItemVariantId, LotId, MovementId, OriginLayerId, LayerDate, PostingSeq, QtyIn, QtyRemaining, ValueRemaining,
            UnitCost, IsNegative, CreatedBy)
        VALUES (@CompanyId, source.WarehouseId, source.DepotId, source.ItemVariantId, source.LotId, source.MovementId, source.OriginLayerId, source.LayerDate,
            COALESCE(source.SeqOverride, source.MovementSeq), source.QtyIn, source.QtyRemaining, source.ValueRemaining, source.UnitCost, source.IsNegative, @UserId)
    OUTPUT source.LayerRef, inserted.Id INTO @LayerMap;

    INSERT INTO inv.CostLayerConsumption (OutboundMovementId, CostLayerId, Qty, CostAmount)
    SELECT x.MovementId, m.LayerId, x.Qty, x.CostAmount FROM @Consumptions AS x JOIN @LayerMap AS m ON m.LayerRef = x.LayerRef;

    UPDATE inv.Stock SET QtyOnHand = @Qty, CostOnHand = @Value, LastUnitCost = @LastUnitCost
    WHERE WarehouseId = @WarehouseId AND DepotKey = ISNULL(@DepotId, 0) AND ItemVariantId = @ItemVariantId AND LotKey = ISNULL(@LotId, 0);

    DELETE FROM inv.CostRecalcQueue
    WHERE WarehouseId = @WarehouseId AND DepotKey = ISNULL(@DepotId, 0) AND ItemVariantId = @ItemVariantId AND LotKey = ISNULL(@LotId, 0);

    SELECT i.WarehouseId, i.DepotId, i.ItemVariantId, i.LotId, MIN(i.MovementDate) AS FromDate
    FROM inv.StockMovement AS i
    JOIN @Updates AS u ON u.MovementId = i.CounterMovementId
    GROUP BY i.WarehouseId, i.DepotId, i.ItemVariantId, i.LotId;
END;
