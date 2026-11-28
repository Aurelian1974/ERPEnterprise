CREATE OR ALTER PROCEDURE inv.usp_DepotWarehouse_Save
    @CompanyId   int,
    @DepotId     int,
    @WarehouseId int,
    @IsActive    bit,
    @UserId      int
AS
BEGIN
    -- Gestiunea intr-un depozit (02.05, S1 3.8): o gestiune poate fi in mai multe depozite, un depozit are mai multe gestiuni. Tranzitul si
    -- gestiunile la terti nu intra in depozite, iar o gestiune legata direct de o locatie nici ea (54017). Coduri: 54001, 54023; scoaterea
    -- gestiunii implicite a depozitului: 54018.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Depot WHERE Id = @DepotId AND CompanyId = @CompanyId)
        THROW 54023, N'Depozitul nu există în firma curentă.', 1;
    DECLARE @Kind tinyint, @LocationId int;
    SELECT @Kind = Kind, @LocationId = LocationId FROM inv.Warehouse WHERE Id = @WarehouseId AND CompanyId = @CompanyId;
    IF @Kind IS NULL
        THROW 54001, N'Gestiunea nu există în firma curentă.', 1;
    IF @IsActive = 1 AND (@Kind IN (3, 5) OR @LocationId IS NOT NULL)
        THROW 54017, N'Doar gestiunile fără locație proprie (depozit, magazin, custodie) intră în depozite.', 1;
    IF @IsActive = 0 AND EXISTS (SELECT 1 FROM inv.Depot WHERE Id = @DepotId AND DefaultWarehouseId = @WarehouseId)
        THROW 54018, N'Gestiunea implicită a depozitului nu se scoate din depozit.', 1;

    IF EXISTS (SELECT 1 FROM inv.DepotWarehouse WITH (UPDLOCK, HOLDLOCK) WHERE DepotId = @DepotId AND WarehouseId = @WarehouseId)
        UPDATE inv.DepotWarehouse SET IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE DepotId = @DepotId AND WarehouseId = @WarehouseId;
    ELSE IF @IsActive = 1
        INSERT INTO inv.DepotWarehouse (DepotId, WarehouseId, CreatedBy) VALUES (@DepotId, @WarehouseId, @UserId);
END;
