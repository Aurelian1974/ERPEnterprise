CREATE OR ALTER PROCEDURE core.usp_Item_Merge
    @KeepId   int,
    @RemoveId int,
    @UserId   int
AS
BEGIN
    -- Unirea a doua articole (02.01, 3.6 punctul 7): codurile de bare, exceptiile de UM (fara cele pe care pastratul le are), codurile
    -- la parteneri, traducerile si proprietatile lipsa trec pe articolul pastrat; garantiile SGR care trimiteau la cel unit trimit la
    -- pastrat. Blocarea si dimensiunile implicite raman ale pastratului. Articolul unit se dezactiveaza cu MergedIntoId; modulele
    -- (stocuri, documente) isi muta datele la evenimentul ArticolUnificat.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @KeepId = @RemoveId
        THROW 50021, N'Alegeți două articole diferite.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Item WITH (UPDLOCK, HOLDLOCK) WHERE Id = @KeepId AND IsActive = 1 AND MergedIntoId IS NULL)
       OR NOT EXISTS (SELECT 1 FROM core.Item WITH (UPDLOCK, HOLDLOCK) WHERE Id = @RemoveId AND MergedIntoId IS NULL)
        THROW 50021, N'Unul dintre articole nu există, e inactiv sau a fost deja unit cu altul.', 1;

    UPDATE core.ItemBarcode SET ItemId = @KeepId, UomId = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE ItemId = @RemoveId;

    UPDATE u SET ItemId = @KeepId
    FROM core.ItemUom AS u
    WHERE u.ItemId = @RemoveId AND NOT EXISTS (SELECT 1 FROM core.ItemUom AS k WHERE k.ItemId = @KeepId AND k.UomId = u.UomId);
    DELETE FROM core.ItemUom WHERE ItemId = @RemoveId;

    UPDATE core.ItemPartnerReference SET ItemId = @KeepId, UomId = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE ItemId = @RemoveId;

    UPDATE t SET ItemId = @KeepId
    FROM core.ItemTranslation AS t
    WHERE t.ItemId = @RemoveId AND NOT EXISTS (SELECT 1 FROM core.ItemTranslation AS k WHERE k.ItemId = @KeepId AND k.LanguageCode = t.LanguageCode);
    DELETE FROM core.ItemTranslation WHERE ItemId = @RemoveId;

    UPDATE v SET EntityId = @KeepId
    FROM core.PropertyValue AS v
    JOIN core.PropertyDefinition AS d ON d.Id = v.PropertyDefinitionId AND d.EntityType = 'Item'
    WHERE v.EntityId = @RemoveId
      AND NOT EXISTS (SELECT 1 FROM core.PropertyValue AS k WHERE k.EntityId = @KeepId AND k.PropertyDefinitionId = v.PropertyDefinitionId);

    UPDATE core.Item SET DepositItemId = @KeepId WHERE DepositItemId = @RemoveId AND Id <> @KeepId;

    UPDATE core.Item SET IsActive = 0, MergedIntoId = @KeepId, DepositItemId = NULL, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @RemoveId;
    UPDATE core.Item SET ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @KeepId;

    SELECT Id, RowVersion FROM core.Item WHERE Id = @KeepId;
END;
