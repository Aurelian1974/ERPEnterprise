CREATE OR ALTER PROCEDURE core.usp_Item_Search
    @Search               nvarchar(250) = NULL,
    @Barcode              varchar(50) = NULL,
    @IsStockable          bit = NULL,
    @ItemType             tinyint = NULL,
    @ItemGroupId          int = NULL,
    @IsActive             bit = NULL,
    @PropertyDefinitionId int = NULL,
    @PropertyOptionId     int = NULL,
    @PropertyBool         bit = NULL,
    @Offset               int = 0,
    @PageSize             int = 50,
    @Export               bit = 0
AS
BEGIN
    -- Lista articolelor (macheta 05; publica pentru module): cautare prefix SARGable dupa denumire (fara majuscule si diacritice)
    -- sau cod, ori exacta dupa codul de bare (@Barcode = textul cautat, cand poate fi cod de bare); filtre pe stocabil, tip, grupa (cu subgrupele ei), stare si o proprietate filtrabila.
    -- @Export = 1 intoarce toate randurile filtrului, cu coloanele exportului Excel.
    SET NOCOUNT ON;

    DECLARE @Groups TABLE (Id int PRIMARY KEY);
    IF @ItemGroupId IS NOT NULL
        WITH Tree AS (
            SELECT Id FROM core.ItemGroup WHERE Id = @ItemGroupId
            UNION ALL
            SELECT g.Id FROM core.ItemGroup AS g JOIN Tree AS t ON g.ParentId = t.Id
        )
        INSERT INTO @Groups (Id) SELECT Id FROM Tree;

    SELECT i.Id, i.Code, i.Name, i.ItemType, i.IsStockable, i.IsActive, v.Code AS VatRateCode, v.[Percent] AS VatPercent,
           u.Code AS BaseUomCode, g.Code AS GroupCode, g.Name AS GroupName,
           (SELECT TOP (1) b.Barcode FROM core.ItemBarcode AS b WHERE b.ItemId = i.Id AND b.IsActive = 1 ORDER BY b.Id) AS Barcode,
           s.Code AS UomSetCode, i.PricePrecision, i.QuantityPrecision, i.CnCode, d.Code AS DepositItemCode,
           i.NetWeightKg, i.GrossWeightKg, i.VolumeM3, COUNT(*) OVER () AS TotalCount
    FROM core.Item AS i
    JOIN core.VatRate AS v ON v.Id = i.DefaultVatRateId
    JOIN core.UnitOfMeasure AS u ON u.Id = i.BaseUomId
    JOIN core.UomSet AS s ON s.Id = i.UomSetId
    LEFT JOIN core.ItemGroup AS g ON g.Id = i.ItemGroupId
    LEFT JOIN core.Item AS d ON d.Id = i.DepositItemId
    WHERE (@Search IS NULL OR i.NameSearch LIKE @Search + N'%' OR i.CodeKey LIKE UPPER(@Search) + N'%'
             OR (@Barcode IS NOT NULL AND EXISTS (SELECT 1 FROM core.ItemBarcode AS b WHERE b.ItemId = i.Id AND b.Barcode = @Barcode)))
      AND (@IsStockable IS NULL OR i.IsStockable = @IsStockable)
      AND (@ItemType IS NULL OR i.ItemType = @ItemType)
      AND (@ItemGroupId IS NULL OR i.ItemGroupId IN (SELECT Id FROM @Groups))
      AND (@IsActive IS NULL OR i.IsActive = @IsActive)
      AND (@PropertyDefinitionId IS NULL OR EXISTS (
            SELECT 1 FROM core.PropertyValue AS pv
            WHERE pv.PropertyDefinitionId = @PropertyDefinitionId AND pv.EntityId = i.Id
              AND (@PropertyOptionId IS NULL OR pv.ValueOptionId = @PropertyOptionId)
              AND (@PropertyBool IS NULL OR pv.ValueBool = @PropertyBool)))
    ORDER BY i.NameSearch, i.Id
    OFFSET CASE WHEN @Export = 1 THEN 0 ELSE @Offset END ROWS
    FETCH NEXT CASE WHEN @Export = 1 THEN 2147483647 ELSE @PageSize END ROWS ONLY
    OPTION (RECOMPILE);

    SELECT COUNT(*) AS ActiveCount FROM core.Item WHERE IsActive = 1;
END;
