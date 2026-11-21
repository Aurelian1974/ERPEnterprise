CREATE OR ALTER PROCEDURE core.usp_ItemTranslation_Save
    @ItemId       int,
    @Translations core.ItemTranslationList READONLY,
    @UserId       int
AS
BEGIN
    -- Traducerile articolului salvate impreuna (02.01, 3.6 punctul 12): lista primita inlocuieste traducerile existente.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @ItemId)
        THROW 50027, N'Articolul nu există.', 1;

    DELETE t FROM core.ItemTranslation AS t
    WHERE t.ItemId = @ItemId AND NOT EXISTS (SELECT 1 FROM @Translations AS x WHERE x.LanguageCode = t.LanguageCode);

    MERGE core.ItemTranslation AS target
    USING (SELECT LanguageCode, Name FROM @Translations) AS source ON target.ItemId = @ItemId AND target.LanguageCode = source.LanguageCode
    WHEN MATCHED AND target.Name <> source.Name THEN UPDATE SET Name = source.Name, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHEN NOT MATCHED BY TARGET THEN INSERT (ItemId, LanguageCode, Name, ModifiedAt, ModifiedBy)
        VALUES (@ItemId, source.LanguageCode, source.Name, SYSUTCDATETIME(), @UserId);
END;
