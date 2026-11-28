CREATE OR ALTER PROCEDURE inv.usp_Depot_Save
    @Id                 int = NULL,
    @CompanyId          int,
    @Code               nvarchar(20),
    @Name               nvarchar(200),
    @LocationId         int,
    @DefaultWarehouseId int = NULL,
    @DimensionValueId   int = NULL,
    @IsActive           bit,
    @RowVersion         binary(8) = NULL,
    @UserId             int
AS
BEGIN
    -- Depozitul (02.05, S1 3.8): intr-o locatie activa a firmei cu nivelul „depozit” (54017); cod unic pe firma (54022); gestiunea implicita e
    -- una din gestiunile depozitului (54018). La creare primeste valoarea de dimensiune „depozit” (creata de apelant). Coduri: 54014, 54023.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inv.Depot WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId)
        THROW 54023, N'Depozitul nu există în firma curentă.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.Location WHERE Id = @LocationId AND CompanyId = @CompanyId AND IsActive = 1)
       OR NOT EXISTS (SELECT 1 FROM inv.ufn_LocationStockLevel(@CompanyId) WHERE LocationId = @LocationId AND StockKeepingLevel = 2)
        THROW 54017, N'Depozitul se creează într-o locație activă a firmei care ține stocul pe depozit.', 1;
    IF @DefaultWarehouseId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM inv.DepotWarehouse
        WHERE DepotId = @Id AND WarehouseId = @DefaultWarehouseId AND IsActive = 1)
        THROW 54018, N'Gestiunea implicită trebuie să fie una din gestiunile depozitului.', 1;
    IF EXISTS (SELECT 1 FROM inv.Depot WITH (UPDLOCK, HOLDLOCK) WHERE CompanyId = @CompanyId AND CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 54022, N'Codul depozitului există deja în firmă.', 1;

    IF @Id IS NULL
    BEGIN
        IF @DimensionValueId IS NULL OR NOT EXISTS (SELECT 1 FROM core.DimensionValue
            WHERE Id = @DimensionValueId AND CompanyId = @CompanyId AND DimensionType = 'DEPOT')
           OR EXISTS (SELECT 1 FROM inv.Depot WHERE DimensionValueId = @DimensionValueId)
            THROW 54018, N'Valoarea de dimensiune „depozit” lipsește sau aparține altui depozit.', 1;
        INSERT INTO inv.Depot (CompanyId, Code, Name, LocationId, DimensionValueId, IsActive, CreatedBy)
        VALUES (@CompanyId, @Code, @Name, @LocationId, @DimensionValueId, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE inv.Depot
        SET Code = @Code, Name = @Name, LocationId = @LocationId, DefaultWarehouseId = @DefaultWarehouseId, IsActive = @IsActive,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;
        IF @@ROWCOUNT = 0
            THROW 54014, N'Depozitul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT Id, RowVersion, DimensionValueId, IsActive FROM inv.Depot WHERE Id = @Id;
END;
