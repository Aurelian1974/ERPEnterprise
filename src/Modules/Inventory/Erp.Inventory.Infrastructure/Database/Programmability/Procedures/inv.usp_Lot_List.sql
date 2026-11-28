CREATE OR ALTER PROCEDURE inv.usp_Lot_List
    @CompanyId          int,
    @ItemVariantId      int = NULL,
    @WarehouseId        int = NULL,
    @Search             nvarchar(60) = NULL,
    @ExpiringWithinDays int = NULL,
    @Today              date,
    @Top                int = 500
AS
BEGIN
    -- Loturile cu stoc (02.05, S4 10, 11; machetele s4-01, s4-02), pe gestiune: CKU-ul, termenul, zilele ramase, starea, cantitatea. Cautarea dupa
    -- codul lotului, CKU sau lotul furnizorului merge pe coloanele ...Search (fara diacritice, fara majuscule, SQL-22).
    SET NOCOUNT ON;

    SELECT TOP (@Top) x.Id AS LotId, x.ItemVariantId, v.Code AS VariantCode, v.Name AS VariantName, x.LotCode, x.CkuCode, x.SupplierLotCode, x.ExpiryDate,
        x.ExpiryKind, CASE WHEN x.ExpiryDate IS NULL THEN NULL ELSE DATEDIFF(DAY, @Today, x.ExpiryDate) END AS DaysLeft, x.Status, x.IsTechnical,
        s.WarehouseId, w.Code AS WarehouseCode, w.DimensionValueId AS WarehouseDimensionId, SUM(s.QtyOnHand) AS QtyOnHand
    FROM inv.Lot AS x
    JOIN core.ItemVariant AS v ON v.Id = x.ItemVariantId
    JOIN inv.Stock AS s ON s.LotId = x.Id AND s.QtyOnHand <> 0
    JOIN inv.Warehouse AS w ON w.Id = s.WarehouseId
    WHERE x.CompanyId = @CompanyId
      AND (@ItemVariantId IS NULL OR x.ItemVariantId = @ItemVariantId)
      AND (@WarehouseId IS NULL OR s.WarehouseId = @WarehouseId)
      AND (@Search IS NULL OR x.LotCodeSearch LIKE N'%' + @Search + N'%' OR x.CkuCodeSearch LIKE N'%' + @Search + N'%'
           OR x.SupplierLotCode LIKE N'%' + @Search + N'%')
      AND (@ExpiringWithinDays IS NULL OR x.ExpiryDate <= DATEADD(DAY, @ExpiringWithinDays, @Today))
    GROUP BY x.Id, x.ItemVariantId, v.Code, v.Name, x.LotCode, x.CkuCode, x.SupplierLotCode, x.ExpiryDate, x.ExpiryKind, x.Status, x.IsTechnical, s.WarehouseId,
        w.Code, w.DimensionValueId
    ORDER BY x.ExpiryDate, v.Code, w.Code;
END;
