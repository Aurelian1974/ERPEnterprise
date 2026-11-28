CREATE OR ALTER PROCEDURE inv.usp_ItemStockSettings_EnsureDefaults
    @ItemId int,
    @UserId int
AS
BEGIN
    -- Consumatorul ArticolModificat (02.05, S1 5.2): randul de setari cu valorile implicite ale firmei, in fiecare firma initializata in Stocuri,
    -- pentru un model stocabil care nu il are inca. Idempotenta; modelele nestocabile nu primesc nimic.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO inv.ItemStockSettings (CompanyId, ItemId, ValuationMethod, CreatedBy)
    SELECT c.CompanyId, i.Id, c.DefaultValuationMethod, @UserId
    FROM inv.StockSettings AS c
    JOIN core.Item AS i ON i.Id = @ItemId AND i.IsStockable = 1
    WHERE NOT EXISTS (SELECT 1 FROM inv.ItemStockSettings AS s WITH (UPDLOCK, HOLDLOCK) WHERE s.CompanyId = c.CompanyId AND s.ItemId = i.Id);
END;
