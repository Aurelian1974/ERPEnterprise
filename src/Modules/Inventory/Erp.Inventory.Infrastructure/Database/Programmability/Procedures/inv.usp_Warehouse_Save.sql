CREATE OR ALTER PROCEDURE inv.usp_Warehouse_Save
    @Id                  int = NULL,
    @CompanyId           int,
    @Code                nvarchar(20),
    @Name                nvarchar(200),
    @LocationId          int = NULL,
    @Kind                tinyint,
    @ValuationBasis      tinyint,
    @RetailPriceListId   int = NULL,
    @ThirdPartyId        int = NULL,
    @NegativeStockPolicy tinyint = NULL,
    @DimensionValueId    int = NULL,
    @DefaultCostCenterId int = NULL,
    @DeliveryOrderMode   tinyint,
    @ReturnWarehouseId   int = NULL,
    @Purpose             tinyint,
    @RowVersion          binary(8) = NULL,
    @UserId              int
AS
BEGIN
    -- Gestiunea firmei curente (02.05, S1 3.1 si 4.1). La creare primeste valoarea de dimensiune „gestiune” (creata de apelant in aceeasi
    -- tranzactie). Firma nu se schimba; tranzitul se creeaza doar automat. Dupa prima miscare: codul, tipul si baza de evaluare raman (54004),
    -- iar locatia se muta doar intr-una de acelasi tip (54003). Coduri: 54001 inexistenta, 54002 cod existent, 54003 locatie, 54007 pret cu
    -- amanuntul, 54012 politica permisiva pe custodie sau tranzit, 54014 versiune, 54017 locatie cu nivelul „depozit”, 54018 referinta
    -- invalida.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @CurrentCompanyId int, @CurrentKind tinyint, @CurrentCode nvarchar(20), @CurrentBasis tinyint, @CurrentLocationId int,
        @FirstMovementAt datetime2(3), @CurrentDimensionValueId int;
    IF @Id IS NOT NULL
    BEGIN
        SELECT @CurrentCompanyId = CompanyId, @CurrentKind = Kind, @CurrentCode = Code, @CurrentBasis = ValuationBasis,
            @CurrentLocationId = LocationId, @FirstMovementAt = FirstMovementAt, @CurrentDimensionValueId = DimensionValueId
        FROM inv.Warehouse WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
        IF @CurrentCompanyId IS NULL OR @CurrentCompanyId <> @CompanyId
            THROW 54001, N'Gestiunea nu există în firma curentă.', 1;
        IF (@CurrentKind = 3 OR @Kind = 3) AND @CurrentKind <> @Kind
            THROW 54004, N'Gestiunea de tranzit își păstrează tipul.', 1;
        IF @FirstMovementAt IS NOT NULL AND (@CurrentKind <> @Kind OR @CurrentBasis <> @ValuationBasis OR @CurrentCode <> @Code)
            THROW 54004, N'Codul, tipul și baza de evaluare nu se mai schimbă după prima mișcare de stoc.', 1;
    END
    ELSE IF @Kind = 3
        THROW 54004, N'Gestiunea de tranzit se creează automat, una pe firmă.', 1;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId AND IsActive = 1)
        THROW 54018, N'Firma nu există sau este inactivă.', 1;

    IF @ValuationBasis = 2 AND (@Kind <> 2 OR @RetailPriceListId IS NULL)
        THROW 54007, N'Evidența la preț cu amănuntul e permisă doar la magazin și cere lista de prețuri cu amănuntul.', 1;

    IF @NegativeStockPolicy IS NULL
        SELECT @NegativeStockPolicy = CASE WHEN @Kind IN (3, 4) THEN 1 ELSE DefaultNegativeStockPolicy END
        FROM inv.StockSettings WHERE CompanyId = @CompanyId;
    SET @NegativeStockPolicy = ISNULL(@NegativeStockPolicy, 1);
    IF @NegativeStockPolicy = 2 AND @Kind IN (3, 4)
        THROW 54012, N'Gestiunile de custodie și de tranzit nu pot avea stoc negativ.', 1;

    IF @Kind IN (1, 2, 4) AND @LocationId IS NULL
    BEGIN
        -- La nivelul „depozit” gestiunea nu sta direct in locatie, ci in depozite (S1 3.8).
        IF NOT EXISTS (SELECT 1 FROM inv.ufn_LocationStockLevel(@CompanyId) WHERE StockKeepingLevel = 2)
            THROW 54003, N'Gestiunea cere o locație activă a firmei.', 1;
    END
    ELSE IF @Kind IN (1, 2, 4)
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM core.Location WHERE Id = @LocationId AND CompanyId = @CompanyId AND IsActive = 1)
            THROW 54003, N'Gestiunea cere o locație activă a firmei.', 1;
        IF EXISTS (SELECT 1 FROM inv.ufn_LocationStockLevel(@CompanyId) WHERE LocationId = @LocationId AND StockKeepingLevel = 2)
           OR (@Id IS NOT NULL AND EXISTS (SELECT 1 FROM inv.DepotWarehouse WHERE WarehouseId = @Id AND IsActive = 1))
            THROW 54017, N'Locația ține stocul pe depozit: gestiunea se leagă de depozitele locației, nu direct de locație.', 1;
        IF @FirstMovementAt IS NOT NULL AND @CurrentLocationId IS NOT NULL AND @CurrentLocationId <> @LocationId
           AND (SELECT LocationType FROM core.Location WHERE Id = @LocationId) <> (SELECT LocationType FROM core.Location WHERE Id = @CurrentLocationId)
            THROW 54003, N'După prima mișcare, gestiunea se mută doar într-o locație de același tip.', 1;
    END
    ELSE IF @LocationId IS NOT NULL
        THROW 54003, N'Gestiunile de tranzit și cele aflate la terți nu au locație.', 1;

    IF @Kind IN (4, 5) AND (@ThirdPartyId IS NULL OR NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @ThirdPartyId AND IsActive = 1))
        THROW 54018, N'Gestiunile de custodie și cele aflate la terți cer partenerul activ care deține sau păstrează bunurile.', 1;
    IF @Kind IN (1, 2, 3) SET @ThirdPartyId = NULL;

    IF @DefaultCostCenterId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.DimensionValue
        WHERE Id = @DefaultCostCenterId AND CompanyId = @CompanyId AND DimensionType = 'COST_CENTER' AND IsActive = 1)
        THROW 54018, N'Centrul de cost implicit trebuie să fie un centru de cost activ al firmei.', 1;
    IF @Purpose = 2 AND @Kind <> 1
        THROW 54018, N'O gestiune de retur de la clienți este de tip depozit.', 1;
    IF @ReturnWarehouseId IS NOT NULL AND (@ReturnWarehouseId = @Id OR NOT EXISTS (SELECT 1 FROM inv.Warehouse
        WHERE Id = @ReturnWarehouseId AND CompanyId = @CompanyId AND Kind = 1 AND Purpose = 2 AND IsActive = 1))
        THROW 54018, N'Gestiunea de retur trebuie să fie o gestiune activă a firmei, de tip depozit, pentru retururi de la clienți.', 1;

    IF EXISTS (SELECT 1 FROM inv.Warehouse WITH (UPDLOCK, HOLDLOCK)
               WHERE CompanyId = @CompanyId AND CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 54002, N'Codul gestiunii există deja în firmă.', 1;

    IF @Id IS NULL
    BEGIN
        IF @DimensionValueId IS NULL OR NOT EXISTS (SELECT 1 FROM core.DimensionValue
            WHERE Id = @DimensionValueId AND CompanyId = @CompanyId AND DimensionType = 'WAREHOUSE')
           OR EXISTS (SELECT 1 FROM inv.Warehouse WHERE DimensionValueId = @DimensionValueId)
            THROW 54018, N'Valoarea de dimensiune „gestiune” lipsește sau aparține altei gestiuni.', 1;

        INSERT INTO inv.Warehouse (CompanyId, Code, Name, LocationId, Kind, ValuationBasis, RetailPriceListId, IsOwnGoods, ThirdPartyId,
            NegativeStockPolicy, DimensionValueId, DefaultCostCenterId, DeliveryOrderMode, ReturnWarehouseId, Purpose, CreatedBy)
        VALUES (@CompanyId, @Code, @Name, @LocationId, @Kind, @ValuationBasis, @RetailPriceListId, CASE WHEN @Kind = 4 THEN 0 ELSE 1 END,
            @ThirdPartyId, @NegativeStockPolicy, @DimensionValueId, @DefaultCostCenterId, @DeliveryOrderMode, @ReturnWarehouseId, @Purpose, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE inv.Warehouse
        SET Code = @Code, Name = @Name, LocationId = @LocationId, Kind = @Kind, ValuationBasis = @ValuationBasis,
            RetailPriceListId = @RetailPriceListId, IsOwnGoods = CASE WHEN @Kind = 4 THEN 0 ELSE 1 END, ThirdPartyId = @ThirdPartyId,
            NegativeStockPolicy = @NegativeStockPolicy, DefaultCostCenterId = @DefaultCostCenterId, DeliveryOrderMode = @DeliveryOrderMode,
            ReturnWarehouseId = @ReturnWarehouseId, Purpose = @Purpose, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
            THROW 54014, N'Gestiunea a fost modificată de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT Id, RowVersion, DimensionValueId, IsActive FROM inv.Warehouse WHERE Id = @Id;
END;
