CREATE OR ALTER PROCEDURE inv.usp_LandedCost_Targets
    @CompanyId    int,
    @MovementIds  inv.BigIdList READONLY,
    @SourceModule varchar(20) = NULL
AS
BEGIN
    -- Intrarile tinta ale unui document CA (02.05, S3 3.3), cu baza de repartizare: valoarea, cantitatea, greutatea (bruta, altfel neta) si volumul
    -- din fisa articolului. Cu @SourceModule, Id-urile sunt liniile sursa (SourceLineId) ale modulului apelant (portul AdjustInboundCostAsync).
    -- Tinta trebuie sa fie o intrare a firmei, nu o primire din transfer, nu o stornare si nestornata (54303).
    SET NOCOUNT ON;

    DECLARE @Targets TABLE (RequestedId bigint PRIMARY KEY, MovementId bigint NULL);
    INSERT INTO @Targets (RequestedId, MovementId)
    SELECT i.Id, CASE WHEN @SourceModule IS NULL THEN m.Id ELSE s.Id END
    FROM @MovementIds AS i
    LEFT JOIN inv.StockMovement AS m ON @SourceModule IS NULL AND m.Id = i.Id AND m.CompanyId = @CompanyId
    LEFT JOIN inv.StockMovement AS s ON @SourceModule IS NOT NULL AND s.SourceModule = @SourceModule AND s.SourceLineId = i.Id AND s.SourceRole = 1
        AND s.ReversalKey = 0 AND s.CompanyId = @CompanyId;

    IF EXISTS (SELECT 1 FROM @Targets WHERE MovementId IS NULL)
        THROW 54303, N'Intrarea vizată de costul adăugat nu există în firma curentă.', 1;

    IF EXISTS (SELECT 1 FROM @Targets AS t JOIN inv.StockMovement AS m ON m.Id = t.MovementId
               WHERE m.QtyBase <= 0 OR m.CounterMovementId IS NOT NULL OR m.ReversalOfId IS NOT NULL
                  OR EXISTS (SELECT 1 FROM inv.StockMovement AS r WHERE r.ReversalOfId = m.Id))
        THROW 54303, N'Costul adăugat se poate repartiza doar pe intrări nestornate (nu pe primiri din transfer sau stornări).', 1;

    SELECT t.RequestedId, m.Id AS MovementId, m.WarehouseId, w.Code AS WarehouseCode, m.DepotId, m.ItemVariantId, v.Code AS VariantCode, m.LotId,
        m.MovementDate, m.QtyBase, m.CostAmount, m.SourceDocumentType, m.SourceDocumentNumber,
        CAST(m.QtyBase * COALESCE(it.GrossWeightKg, it.NetWeightKg) AS decimal(19,6)) AS WeightKg,
        CAST(m.QtyBase * it.VolumeM3 AS decimal(19,6)) AS VolumeM3
    FROM @Targets AS t
    JOIN inv.StockMovement AS m ON m.Id = t.MovementId
    JOIN inv.Warehouse AS w ON w.Id = m.WarehouseId
    JOIN core.ItemVariant AS v ON v.Id = m.ItemVariantId
    JOIN core.Item AS it ON it.Id = v.ItemId
    ORDER BY t.RequestedId;
END;
