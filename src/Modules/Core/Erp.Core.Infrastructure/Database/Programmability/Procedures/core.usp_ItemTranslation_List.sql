CREATE OR ALTER PROCEDURE core.usp_ItemTranslation_List
    @ItemId int
AS
BEGIN
    -- Traducerile denumirii articolului (02.01, 3.6 punctul 12).
    SET NOCOUNT ON;

    SELECT t.LanguageCode, t.Name
    FROM core.ItemTranslation AS t
    WHERE t.ItemId = @ItemId
    ORDER BY t.LanguageCode;
END;
