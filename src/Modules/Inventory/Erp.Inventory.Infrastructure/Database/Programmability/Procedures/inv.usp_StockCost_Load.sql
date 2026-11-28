CREATE OR ALTER PROCEDURE inv.usp_StockCost_Load
    @CompanyId  int,
    @Keys       inv.tt_CostKey READONLY,
    @ReversalOf inv.BigIdList READONLY
AS
BEGIN
    -- Starea de cost a cheilor unui apel (02.05, S3 4.1), in tranzactia apelantului: randurile de sold create daca lipsesc si blocate in ordinea
    -- cheii (UPDLOCK, SP-16), metoda (a modelului pe firma; gestiunile la pret cu amanuntul cu descarcarea prin K la cost mediu, S3 3.7), costul de rezerva (media
    -- variantei in firma, apoi costul standard), straturile deschise si, pentru stornari, miscarile stornate cu consumurile si stratul lor.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO inv.Stock (CompanyId, WarehouseId, DepotId, ItemVariantId, LotId)
    SELECT DISTINCT @CompanyId, k.WarehouseId, k.DepotId, k.ItemVariantId, k.LotId
    FROM @Keys AS k
    WHERE NOT EXISTS (SELECT 1 FROM inv.Stock AS s WITH (UPDLOCK, HOLDLOCK)
                      WHERE s.WarehouseId = k.WarehouseId AND s.DepotKey = ISNULL(k.DepotId, 0) AND s.ItemVariantId = k.ItemVariantId
                        AND s.LotKey = ISNULL(k.LotId, 0));

    DECLARE @Locked TABLE (StockId bigint PRIMARY KEY);
    INSERT INTO @Locked
    SELECT s.Id FROM inv.Stock AS s WITH (UPDLOCK, ROWLOCK)
    WHERE EXISTS (SELECT 1 FROM @Keys AS k WHERE k.WarehouseId = s.WarehouseId AND ISNULL(k.DepotId, 0) = s.DepotKey AND k.ItemVariantId = s.ItemVariantId
                  AND ISNULL(k.LotId, 0) = s.LotKey)
    ORDER BY s.WarehouseId, s.DepotKey, s.ItemVariantId, s.LotKey;

    SELECT s.WarehouseId, s.DepotKey, s.ItemVariantId, s.LotKey, s.QtyOnHand, s.CostOnHand, s.LastUnitCost,
        CAST(CASE WHEN w.ValuationBasis = 2 AND COALESCE(w.RetailDischargeMethod, ss.RetailDischargeMethod, 1) = 1 THEN 2 ELSE COALESCE(iss.ValuationMethod, ss.DefaultValuationMethod, 2) END AS tinyint) AS Method,
        COALESCE((SELECT SUM(x.CostOnHand) / NULLIF(SUM(x.QtyOnHand), 0) FROM inv.Stock AS x
                  WHERE x.CompanyId = @CompanyId AND x.ItemVariantId = s.ItemVariantId AND x.QtyOnHand > 0), iss.StandardCost) AS FallbackUnitCost
    FROM inv.Stock AS s
    JOIN @Locked AS l ON l.StockId = s.Id
    JOIN inv.Warehouse AS w ON w.Id = s.WarehouseId
    JOIN core.ItemVariant AS v ON v.Id = s.ItemVariantId
    LEFT JOIN inv.ItemStockSettings AS iss ON iss.CompanyId = @CompanyId AND iss.ItemId = v.ItemId
    LEFT JOIN inv.StockSettings AS ss ON ss.CompanyId = @CompanyId;

    SELECT c.Id, c.WarehouseId, c.DepotKey, c.ItemVariantId, c.LotKey, c.LayerDate, c.PostingSeq, c.QtyIn, c.QtyRemaining, c.ValueRemaining, c.UnitCost,
        c.IsNegative, c.MovementId, c.OriginLayerId
    FROM inv.CostLayer AS c
    WHERE (c.QtyRemaining <> 0 AND EXISTS (SELECT 1 FROM @Keys AS k WHERE k.WarehouseId = c.WarehouseId AND ISNULL(k.DepotId, 0) = c.DepotKey
                                           AND k.ItemVariantId = c.ItemVariantId AND ISNULL(k.LotId, 0) = c.LotKey))
       OR c.MovementId IN (SELECT Id FROM @ReversalOf)
       OR c.Id IN (SELECT x.CostLayerId FROM inv.CostLayerConsumption AS x WHERE x.OutboundMovementId IN (SELECT Id FROM @ReversalOf));

    SELECT m.Id AS MovementId, m.CostAmount FROM inv.StockMovement AS m WHERE m.Id IN (SELECT Id FROM @ReversalOf);

    SELECT x.OutboundMovementId, x.CostLayerId, x.Qty, x.CostAmount FROM inv.CostLayerConsumption AS x WHERE x.OutboundMovementId IN (SELECT Id FROM @ReversalOf);
END;
