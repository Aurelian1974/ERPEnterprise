CREATE OR ALTER PROCEDURE inv.usp_ValuationMethodChange_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Documentul SM (macheta s1-04): antetul, modelele cu metoda veche (actuala in ciorna) si previzualizarea pe gestiuni: stocul fiecarei chei la
    -- cost a modelelor, straturile deschise, valoarea si costul mediu (valoarea ramane aceeasi). Gestiunile la pret cu amanuntul prin K nu intra.
    SET NOCOUNT ON;

    SELECT d.Id, d.Number, d.NewMethod, d.EffectiveDate, d.Reason, d.Status, d.CreatedAt, u.DisplayName AS CreatedByName, d.RowVersion
    FROM inv.ValuationMethodChange AS d
    LEFT JOIN core.[User] AS u ON u.Id = d.CreatedBy
    WHERE d.Id = @Id AND d.CompanyId = @CompanyId;

    SELECT l.ItemId, i.Code AS ItemCode, i.Name AS ItemName,
        CAST(CASE WHEN d.Status = 2 THEN l.OldMethod ELSE COALESCE(s.ValuationMethod, ss.DefaultValuationMethod, 2) END AS tinyint) AS OldMethod
    FROM inv.ValuationMethodChangeLine AS l
    JOIN inv.ValuationMethodChange AS d ON d.Id = l.ChangeId
    JOIN core.Item AS i ON i.Id = l.ItemId
    LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = d.CompanyId AND s.ItemId = l.ItemId
    LEFT JOIN inv.StockSettings AS ss ON ss.CompanyId = d.CompanyId
    WHERE l.ChangeId = @Id AND d.CompanyId = @CompanyId
    ORDER BY i.Code;

    SELECT v.Code AS VariantCode, v.Name AS VariantName, w.Code AS WarehouseCode, dp.Code AS DepotCode, k.QtyOnHand AS Qty,
        (SELECT COUNT(*) FROM inv.CostLayer AS c WHERE c.WarehouseId = k.WarehouseId AND c.DepotKey = k.DepotKey AND c.ItemVariantId = k.ItemVariantId
            AND c.LotKey = k.LotKey AND c.QtyRemaining <> 0) AS OpenLayers,
        k.CostOnHand AS Value, CAST(CASE WHEN k.QtyOnHand <> 0 THEN k.CostOnHand / k.QtyOnHand END AS decimal(19,6)) AS AverageCost
    FROM inv.Stock AS k
    JOIN core.ItemVariant AS v ON v.Id = k.ItemVariantId
    JOIN inv.ValuationMethodChangeLine AS l ON l.ItemId = v.ItemId AND l.ChangeId = @Id
    JOIN inv.Warehouse AS w ON w.Id = k.WarehouseId AND w.CompanyId = @CompanyId
    LEFT JOIN inv.Depot AS dp ON dp.Id = k.DepotId
    LEFT JOIN inv.StockSettings AS ss ON ss.CompanyId = w.CompanyId
    WHERE k.QtyOnHand <> 0 AND w.Kind <> 4
      AND NOT (w.ValuationBasis = 2 AND COALESCE(w.RetailDischargeMethod, ss.RetailDischargeMethod, 1) = 1)
    ORDER BY v.Code, w.Code;
END;
