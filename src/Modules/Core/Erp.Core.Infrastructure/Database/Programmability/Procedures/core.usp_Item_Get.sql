CREATE OR ALTER PROCEDURE core.usp_Item_Get
    @Id int
AS
BEGIN
    -- Fisa articolului (macheta 05/05b): datele generale, exceptiile de UM, codurile de bare si valorile proprietatilor.
    SET NOCOUNT ON;

    SELECT i.Id, i.Code, i.Name, i.ItemType, i.UomSetId, i.BaseUomId, i.DefaultVatRateId, i.ItemGroupId, i.PricePrecision, i.QuantityPrecision,
           i.IsStockable, i.CnCode, i.DepositItemId, d.Code AS DepositItemCode, d.Name AS DepositItemName, i.NetWeightKg, i.GrossWeightKg, i.VolumeM3,
           i.IsActive, i.CreatedAt, COALESCE(m.DisplayName, cr.DisplayName) AS ModifiedByName, COALESCE(i.ModifiedAt, i.CreatedAt) AS LastChangedAt,
           i.RowVersion
    FROM core.Item AS i
    JOIN core.[User] AS cr ON cr.Id = i.CreatedBy
    LEFT JOIN core.[User] AS m ON m.Id = i.ModifiedBy
    LEFT JOIN core.Item AS d ON d.Id = i.DepositItemId
    WHERE i.Id = @Id;

    SELECT x.UomId, x.FactorToBase, CAST(0 AS bit) AS IsBase, x.IsDefaultSales, x.IsDefaultPurchase, x.IsDefaultPos, 0 AS SortOrder
    FROM core.ItemUom AS x
    WHERE x.ItemId = @Id
    ORDER BY x.FactorToBase;

    SELECT b.Id, b.ItemId, b.Barcode, b.BarcodeType, b.UomId, b.IsActive, b.RowVersion
    FROM core.ItemBarcode AS b
    WHERE b.ItemId = @Id
    ORDER BY b.IsActive DESC, b.Id;

    SELECT v.PropertyDefinitionId, v.ValueText, v.ValueNumber, v.ValueDate, v.ValueBool, v.ValueOptionId, v.ValueUserId
    FROM core.PropertyValue AS v
    JOIN core.PropertyDefinition AS d ON d.Id = v.PropertyDefinitionId AND d.EntityType = 'Item'
    WHERE v.EntityId = @Id
    ORDER BY v.PropertyDefinitionId, v.Id;
END;
