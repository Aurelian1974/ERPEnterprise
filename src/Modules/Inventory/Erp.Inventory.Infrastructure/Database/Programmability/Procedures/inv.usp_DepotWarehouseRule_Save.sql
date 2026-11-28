CREATE OR ALTER PROCEDURE inv.usp_DepotWarehouseRule_Save
    @CompanyId   int,
    @DepotId     int,
    @ItemGroupId int = NULL,
    @ItemId      int = NULL,
    @WarehouseId int = NULL,
    @UserId      int
AS
BEGIN
    -- Regula de propunere a gestiunii liniei intr-un depozit (02.05, S1 3.8): pe grupa de articole sau pe articol; @WarehouseId NULL sterge regula.
    -- Gestiunea e una activa din depozit (54018). Coduri: 54023.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Depot WHERE Id = @DepotId AND CompanyId = @CompanyId)
        THROW 54023, N'Depozitul nu există în firma curentă.', 1;
    IF (@ItemGroupId IS NULL AND @ItemId IS NULL) OR (@ItemGroupId IS NOT NULL AND @ItemId IS NOT NULL)
        THROW 54018, N'Regula este pe o grupă de articole sau pe un articol.', 1;
    IF @WarehouseId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inv.DepotWarehouse WHERE DepotId = @DepotId AND WarehouseId = @WarehouseId AND IsActive = 1)
        THROW 54018, N'Gestiunea propusă trebuie să fie una din gestiunile depozitului.', 1;

    DELETE FROM inv.DepotWarehouseRule
    WHERE DepotId = @DepotId AND ((@ItemId IS NOT NULL AND ItemId = @ItemId) OR (@ItemId IS NULL AND ItemId IS NULL AND ItemGroupId = @ItemGroupId));
    IF @WarehouseId IS NOT NULL
        INSERT INTO inv.DepotWarehouseRule (DepotId, ItemGroupId, ItemId, WarehouseId, CreatedBy)
        VALUES (@DepotId, @ItemGroupId, @ItemId, @WarehouseId, @UserId);
END;
