CREATE OR ALTER PROCEDURE inv.usp_ItemWarehouseSettings_Save
    @CompanyId           int,
    @Id                  int = NULL,
    @WarehouseId         int,
    @ItemId              int,
    @ItemVariantId       int = NULL,
    @MinQty              decimal(19,6) = NULL,
    @MaxQty              decimal(19,6) = NULL,
    @ReorderQty          decimal(19,6) = NULL,
    @ReplenishmentMethod tinyint = NULL,
    @IsBlockedForIssue   bit,
    @IsBlockedForReceipt bit,
    @IsListed            bit,
    @RowVersion          binary(8) = NULL,
    @UserId              int
AS
BEGIN
    -- Pragurile si blocarile unui model sau ale unei variante intr-o gestiune (02.05, S1 3.4). Gestiunea e a firmei (54001), modelul stocabil si
    -- varianta a modelului (54018); un rand pe (gestiune, model) si unul pe (gestiune, varianta) (54021). Versiune diferita: 54014.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id = @WarehouseId AND CompanyId = @CompanyId)
        THROW 54001, N'Gestiunea nu există în firma curentă.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @ItemId AND IsStockable = 1)
        THROW 54018, N'Articolul nu există sau nu este stocabil.', 1;
    IF @ItemVariantId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.ItemVariant WHERE Id = @ItemVariantId AND ItemId = @ItemId)
        THROW 54018, N'Varianta nu aparține articolului.', 1;
    IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inv.ItemWarehouseSettings AS x JOIN inv.Warehouse AS w ON w.Id = x.WarehouseId
                                       WHERE x.Id = @Id AND w.CompanyId = @CompanyId AND x.ItemId = @ItemId)
        THROW 54001, N'Pragurile nu există în firma curentă.', 1;
    IF EXISTS (SELECT 1 FROM inv.ItemWarehouseSettings WITH (UPDLOCK, HOLDLOCK)
               WHERE WarehouseId = @WarehouseId AND (@Id IS NULL OR Id <> @Id)
                 AND ((@ItemVariantId IS NULL AND ItemVariantId IS NULL AND ItemId = @ItemId) OR ItemVariantId = @ItemVariantId))
        THROW 54021, N'Gestiunea are deja praguri pentru acest articol sau această variantă.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO inv.ItemWarehouseSettings (WarehouseId, ItemId, ItemVariantId, MinQty, MaxQty, ReorderQty, ReplenishmentMethod, IsBlockedForIssue,
            IsBlockedForReceipt, IsListed, CreatedBy)
        VALUES (@WarehouseId, @ItemId, @ItemVariantId, @MinQty, @MaxQty, @ReorderQty, @ReplenishmentMethod, @IsBlockedForIssue, @IsBlockedForReceipt,
            @IsListed, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE inv.ItemWarehouseSettings
        SET WarehouseId = @WarehouseId, ItemVariantId = @ItemVariantId, MinQty = @MinQty, MaxQty = @MaxQty, ReorderQty = @ReorderQty,
            ReplenishmentMethod = @ReplenishmentMethod, IsBlockedForIssue = @IsBlockedForIssue, IsBlockedForReceipt = @IsBlockedForReceipt,
            IsListed = @IsListed, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 54014, N'Pragurile au fost modificate de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT Id, RowVersion FROM inv.ItemWarehouseSettings WHERE Id = @Id;
END;
