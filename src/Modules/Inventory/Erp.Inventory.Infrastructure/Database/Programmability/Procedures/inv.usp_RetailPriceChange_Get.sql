CREATE OR ALTER PROCEDURE inv.usp_RetailPriceChange_Get
    @CompanyId int,
    @Id        int
AS
BEGIN
    -- Documentul MP (02.05, S3 11; macheta s3-04): antetul si liniile; in ciorna, stocul curent si pretul in vigoare, dupa validare cele inregistrate.
    SET NOCOUNT ON;

    SELECT d.Id, d.Number, d.WarehouseId, w.Code AS WarehouseCode, d.DocumentDate, d.Status, d.Notes, d.DiffAmount, d.DiffVatAmount, d.RowVersion
    FROM inv.RetailPriceChange AS d
    JOIN inv.Warehouse AS w ON w.Id = d.WarehouseId
    WHERE d.Id = @Id AND d.CompanyId = @CompanyId;

    SELECT l.LineNumber, l.ItemVariantId, v.Code AS VariantCode, v.Name AS VariantName, l.NewPrice,
        CASE WHEN d.Status = 2 THEN l.OldPrice ELSE p.Price END AS OldPrice,
        CASE WHEN d.Status = 2 THEN l.Qty ELSE (SELECT SUM(s.QtyOnHand) FROM inv.Stock AS s WHERE s.WarehouseId = d.WarehouseId AND s.ItemVariantId = l.ItemVariantId) END AS Qty,
        l.VatPercent, l.DiffAmount, l.DiffVatAmount
    FROM inv.RetailPriceChangeLine AS l
    JOIN inv.RetailPriceChange AS d ON d.Id = l.RetailPriceChangeId
    JOIN core.ItemVariant AS v ON v.Id = l.ItemVariantId
    OUTER APPLY (SELECT TOP (1) x.Price FROM inv.RetailPrice AS x
                 WHERE x.WarehouseId = d.WarehouseId AND x.ItemVariantId = l.ItemVariantId AND x.ValidFrom <= d.DocumentDate ORDER BY x.ValidFrom DESC) AS p
    WHERE l.RetailPriceChangeId = @Id AND d.CompanyId = @CompanyId
    ORDER BY l.LineNumber;
END;
