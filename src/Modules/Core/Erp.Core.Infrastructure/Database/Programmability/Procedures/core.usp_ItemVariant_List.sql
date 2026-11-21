CREATE OR ALTER PROCEDURE core.usp_ItemVariant_List
    @ItemId int
AS
BEGIN
    -- Tab-ul „Variante” al articolului (02.01, 3.6 punctul 13): variantele cu valorile lor (ValueIds, ca „3,12”) si numarul de coduri
    -- de bare; apoi atributele folosite de variante.
    SET NOCOUNT ON;

    SELECT v.Id, v.ItemId, v.Code, v.Name, v.ValueKey AS ValueIds, v.IsActive,
           (SELECT COUNT(*) FROM core.ItemBarcode AS b WHERE b.VariantId = v.Id AND b.IsActive = 1) AS BarcodeCount, v.RowVersion
    FROM core.ItemVariant AS v
    WHERE v.ItemId = @ItemId
    ORDER BY v.IsActive DESC, v.CodeKey;

    SELECT DISTINCT x.AttributeId
    FROM core.ItemVariantValue AS x
    JOIN core.ItemVariant AS v ON v.Id = x.VariantId
    WHERE v.ItemId = @ItemId;
END;
