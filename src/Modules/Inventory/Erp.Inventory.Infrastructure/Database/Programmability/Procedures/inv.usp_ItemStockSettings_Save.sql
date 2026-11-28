CREATE OR ALTER PROCEDURE inv.usp_ItemStockSettings_Save
    @CompanyId           int,
    @ItemId              int,
    @DefaultWarehouseId  int = NULL,
    @ValuationMethod     tinyint,
    @TrackingMode        tinyint,
    @MinQty              decimal(19,6) = NULL,
    @MaxQty              decimal(19,6) = NULL,
    @ReplenishmentMethod tinyint = NULL,
    @IsBlockedForIssue   bit,
    @RowVersion          binary(8) = NULL,
    @UserId              int
AS
BEGIN
    -- Setarile de stoc ale modelului pe firma (02.05, S1 3.4). Modelul e stocabil, gestiunea implicita e a firmei, activa, depozit sau magazin
    -- (54018). Identificarea directa cere lot sau serie (54015). Metoda unui model deja configurat se schimba doar prin documentul de schimbare
    -- (54011, S1 4.5); urmarirea doar fara stoc (54010; verificarea pe stoc se adauga cu S2). Versiune diferita: 54014.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @ItemId AND IsStockable = 1)
        THROW 54018, N'Articolul nu există sau nu este stocabil.', 1;
    IF @DefaultWarehouseId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inv.Warehouse
        WHERE Id = @DefaultWarehouseId AND CompanyId = @CompanyId AND IsActive = 1 AND Kind IN (1, 2))
        THROW 54018, N'Gestiunea implicită trebuie să fie o gestiune activă a firmei, de tip depozit sau magazin.', 1;
    IF @ValuationMethod = 4 AND @TrackingMode = 0
        THROW 54015, N'Identificarea directă cere urmărirea pe lot sau pe serie.', 1;
    SET @ReplenishmentMethod = ISNULL(@ReplenishmentMethod, CASE WHEN @MinQty IS NOT NULL OR @MaxQty IS NOT NULL THEN 1 ELSE 0 END);

    DECLARE @Id int, @CurrentMethod tinyint;
    SELECT @Id = Id, @CurrentMethod = ValuationMethod FROM inv.ItemStockSettings WITH (UPDLOCK, HOLDLOCK)
    WHERE CompanyId = @CompanyId AND ItemId = @ItemId;

    IF @Id IS NULL
    BEGIN
        INSERT INTO inv.ItemStockSettings (CompanyId, ItemId, DefaultWarehouseId, ValuationMethod, TrackingMode, MinQty, MaxQty, ReplenishmentMethod,
            IsBlockedForIssue, CreatedBy)
        VALUES (@CompanyId, @ItemId, @DefaultWarehouseId, @ValuationMethod, @TrackingMode, @MinQty, @MaxQty, @ReplenishmentMethod,
            @IsBlockedForIssue, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @CurrentMethod <> @ValuationMethod
            THROW 54011, N'Metoda de evaluare se schimbă doar prin documentul de schimbare a metodei.', 1;

        UPDATE inv.ItemStockSettings
        SET DefaultWarehouseId = @DefaultWarehouseId, TrackingMode = @TrackingMode, MinQty = @MinQty, MaxQty = @MaxQty,
            ReplenishmentMethod = @ReplenishmentMethod, IsBlockedForIssue = @IsBlockedForIssue, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 54014, N'Setările de stoc ale articolului au fost modificate de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT Id, RowVersion FROM inv.ItemStockSettings WHERE Id = @Id;
END;
