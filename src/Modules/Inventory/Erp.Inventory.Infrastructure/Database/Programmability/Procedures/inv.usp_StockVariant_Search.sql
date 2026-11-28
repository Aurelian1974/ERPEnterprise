CREATE OR ALTER PROCEDURE inv.usp_StockVariant_Search
    @CompanyId   int,
    @Search      nvarchar(100) = NULL,
    @WarehouseId int = NULL,
    @Top         int = 30
AS
BEGIN
    -- Alegerea variantei pe linia unui document de stoc (02.05, S2 4.2 pasul 1): variantele active ale modelelor stocabile, dupa cod sau denumire
    -- (@Search vine escapat pentru LIKE), cu stocul din gestiunea aleasa.
    SET NOCOUNT ON;

    SELECT TOP (@Top) v.Id, v.Code, v.Name, i.Id AS ItemId, u.Code AS BaseUomCode,
        (SELECT SUM(s.QtyOnHand) FROM inv.Stock AS s WHERE s.WarehouseId = @WarehouseId AND s.ItemVariantId = v.Id AND s.CompanyId = @CompanyId) AS OnHand
    FROM core.ItemVariant AS v
    JOIN core.Item AS i ON i.Id = v.ItemId
    JOIN core.UnitOfMeasure AS u ON u.Id = i.BaseUomId
    WHERE v.IsActive = 1 AND i.IsStockable = 1 AND i.IsActive = 1
      AND (@Search IS NULL OR v.CodeKey LIKE UPPER(@Search) + N'%' OR v.Name LIKE N'%' + @Search + N'%' OR i.Name LIKE N'%' + @Search + N'%')
    ORDER BY v.CodeKey;
END;
