CREATE OR ALTER PROCEDURE inv.usp_CostRecalc_Enqueue
    @CompanyId int,
    @Keys      inv.tt_CostKey READONLY,
    @FromDate  date
AS
BEGIN
    -- Coada de recalculare (02.05, S3 3.5): cheile unde s-a inregistrat o miscare cu data anterioara altor miscari ale cheii intra in coada de la
    -- acea data (o singura intrare pe cheie, cu cea mai veche data). Intoarce cheile din coada, cu numarul de miscari de refacut si setarea firmei.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    MERGE inv.CostRecalcQueue WITH (HOLDLOCK) AS target
    USING (SELECT DISTINCT k.WarehouseId, k.DepotId, k.ItemVariantId, k.LotId FROM @Keys AS k
           WHERE EXISTS (SELECT 1 FROM inv.StockMovement AS m WHERE m.WarehouseId = k.WarehouseId AND ISNULL(m.DepotId, 0) = ISNULL(k.DepotId, 0)
                         AND m.ItemVariantId = k.ItemVariantId AND ISNULL(m.LotId, 0) = ISNULL(k.LotId, 0) AND m.MovementDate > @FromDate)) AS source
    ON target.WarehouseId = source.WarehouseId AND target.DepotKey = ISNULL(source.DepotId, 0) AND target.ItemVariantId = source.ItemVariantId
       AND target.LotKey = ISNULL(source.LotId, 0)
    WHEN MATCHED AND target.FromDate > @FromDate THEN UPDATE SET FromDate = @FromDate
    WHEN NOT MATCHED THEN INSERT (CompanyId, WarehouseId, DepotId, ItemVariantId, LotId, FromDate)
        VALUES (@CompanyId, source.WarehouseId, source.DepotId, source.ItemVariantId, source.LotId, @FromDate);

    SELECT q.WarehouseId, q.DepotId, q.ItemVariantId, q.LotId, q.FromDate,
        (SELECT COUNT(*) FROM inv.StockMovement AS m WHERE m.WarehouseId = q.WarehouseId AND ISNULL(m.DepotId, 0) = q.DepotKey
            AND m.ItemVariantId = q.ItemVariantId AND ISNULL(m.LotId, 0) = q.LotKey AND m.MovementDate >= q.FromDate) AS MovementCount,
        ISNULL((SELECT CostRecalcScope FROM inv.StockSettings WHERE CompanyId = @CompanyId), 1) AS CostRecalcScope
    FROM inv.CostRecalcQueue AS q
    WHERE q.CompanyId = @CompanyId AND EXISTS (SELECT 1 FROM @Keys AS k WHERE k.WarehouseId = q.WarehouseId AND ISNULL(k.DepotId, 0) = q.DepotKey
                                               AND k.ItemVariantId = q.ItemVariantId AND ISNULL(k.LotId, 0) = q.LotKey);
END;
