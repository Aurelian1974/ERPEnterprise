CREATE OR ALTER PROCEDURE core.usp_Item_GetName
    @ItemId   int,
    @Language char(2) = NULL
AS
BEGIN
    -- Denumirea articolului pentru un document (IItemCatalog): traducerea in limba ceruta, daca exista, altfel denumirea in romana.
    SET NOCOUNT ON;

    SELECT COALESCE(t.Name, i.Name) AS Name
    FROM core.Item AS i
    LEFT JOIN core.ItemTranslation AS t ON t.ItemId = i.Id AND t.LanguageCode = @Language
    WHERE i.Id = @ItemId;
END;
