CREATE OR ALTER PROCEDURE core.usp_Item_Save
    @Id                int = NULL,
    @Code              nvarchar(50),
    @Name              nvarchar(250),
    @ItemType          tinyint,
    @UomSetId          int,
    @DefaultVatRateId  int,
    @ItemGroupId       int = NULL,
    @PricePrecision    tinyint = NULL,
    @QuantityPrecision tinyint = NULL,
    @IsStockable       bit,
    @CnCode            nvarchar(10) = NULL,
    @DepositItemId     int = NULL,
    @NetWeightKg       decimal(19,6) = NULL,
    @GrossWeightKg     decimal(19,6) = NULL,
    @VolumeM3          decimal(19,6) = NULL,
    @IsActive          bit,
    @SaveUoms          bit = 0,
    @Uoms              core.UomLineList READONLY,
    @SaveProperties    bit = 0,
    @Properties        core.PropertyValueList READONLY,
    @SaveOwner         bit = 0,
    @OwnerCompanyId    int = NULL,
    @OwnerBranchId     int = NULL,
    @OwnerLocationId   int = NULL,
    @RowVersion        binary(8) = NULL,
    @UserId            int
AS
BEGIN
    -- Articolul (02.01, 3.3): cod unic fara diferenta de majuscule; UM de baza copiata din set (linia de baza); cota TVA in vigoare;
    -- ambalajul SGR e un articol activ de tip ambalaj. Cu @SaveUoms, exceptiile de UM (doar UM din afara bazei setului) se inlocuiesc;
    -- cu @SaveProperties, valorile proprietatilor articolului se inlocuiesc. Ruleaza in tranzactia comenzii.
    -- Proprietarul (21.11.2026): NULL = comun; altfel firma, cu punctul de lucru sau locatia ei. Un articol / partener nou il
    -- primeste mereu; la modificare se schimba doar cu @SaveOwner = 1 (importul il pastreaza). Accesul se verifica inainte, in
    -- core.usp_OwnerAccess_Check.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @BaseUomId int = (SELECT l.UomId FROM core.UomSetLine AS l JOIN core.UomSet AS s ON s.Id = l.UomSetId
                              WHERE l.UomSetId = @UomSetId AND l.IsBase = 1 AND (s.IsActive = 1 OR @Id IS NOT NULL));
    IF @BaseUomId IS NULL
        THROW 50021, N'Setul de UM nu există, e inactiv sau nu are UM de bază.', 1;

    IF NOT EXISTS (SELECT 1 FROM core.VatRate WHERE Id = @DefaultVatRateId
                   AND (ValidTo IS NULL OR ValidTo >= CAST(SYSUTCDATETIME() AS date)
                        OR (@Id IS NOT NULL AND EXISTS (SELECT 1 FROM core.Item WHERE Id = @Id AND DefaultVatRateId = @DefaultVatRateId))))
        THROW 50021, N'Cota TVA aleasă nu există sau nu mai e în vigoare.', 1;

    IF @ItemGroupId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.ItemGroup WHERE Id = @ItemGroupId AND IsActive = 1)
        THROW 50021, N'Grupa aleasă nu există sau este inactivă.', 1;

    IF @DepositItemId IS NOT NULL AND (@DepositItemId = @Id
        OR NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @DepositItemId AND ItemType = 7 AND IsActive = 1))
        THROW 50021, N'Garanția SGR este un articol activ de tip ambalaj, diferit de articol.', 1;

    IF EXISTS (SELECT 1 FROM core.Item WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50004, N'Codul de articol există deja.', 1;

    IF @SaveUoms = 1 AND (
        EXISTS (SELECT 1 FROM @Uoms WHERE FactorToBase <= 0 OR UomId = @BaseUomId)
        OR EXISTS (SELECT UomId FROM @Uoms GROUP BY UomId HAVING COUNT(*) > 1)
        OR (SELECT COUNT(*) FROM @Uoms WHERE IsDefaultSales = 1) > 1
        OR (SELECT COUNT(*) FROM @Uoms WHERE IsDefaultPurchase = 1) > 1
        OR (SELECT COUNT(*) FROM @Uoms WHERE IsDefaultPos = 1) > 1
        OR EXISTS (SELECT 1 FROM @Uoms AS u WHERE NOT EXISTS (SELECT 1 FROM core.UnitOfMeasure AS m WHERE m.Id = u.UomId)))
        THROW 50021, N'Excepțiile de UM: factori pozitivi, alte UM decât cea de bază, fiecare o dată, cel mult o implicită pe circuit.', 1;

    IF @SaveProperties = 1 AND EXISTS (
        SELECT 1
        FROM @Properties AS p
        LEFT JOIN core.PropertyDefinition AS d ON d.Id = p.PropertyDefinitionId AND d.EntityType = 'Item' AND d.IsActive = 1
        LEFT JOIN core.PropertyOption AS o ON o.Id = p.ValueOptionId AND o.PropertyDefinitionId = p.PropertyDefinitionId
        LEFT JOIN core.[User] AS u ON u.Id = p.ValueUserId
        WHERE d.Id IS NULL OR (p.ValueOptionId IS NOT NULL AND o.Id IS NULL) OR (p.ValueUserId IS NOT NULL AND u.Id IS NULL))
        THROW 50021, N'O proprietate sau o valoare aleasă nu există.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.Item (Code, Name, ItemType, UomSetId, BaseUomId, DefaultVatRateId, ItemGroupId, PricePrecision, QuantityPrecision,
            IsStockable, CnCode, DepositItemId, NetWeightKg, GrossWeightKg, VolumeM3, IsActive, OwnerCompanyId, OwnerBranchId, OwnerLocationId, CreatedBy)
        VALUES (@Code, @Name, @ItemType, @UomSetId, @BaseUomId, @DefaultVatRateId, @ItemGroupId, @PricePrecision, @QuantityPrecision,
            @IsStockable, @CnCode, @DepositItemId, @NetWeightKg, @GrossWeightKg, @VolumeM3, @IsActive, @OwnerCompanyId, @OwnerBranchId, @OwnerLocationId,
            @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.Item WHERE DepositItemId = @Id AND IsActive = 1)
            THROW 50010, N'Ambalajul SGR e folosit de articole active.', 1;

        UPDATE core.Item
        SET Code = @Code, Name = @Name, ItemType = @ItemType, UomSetId = @UomSetId, BaseUomId = @BaseUomId, DefaultVatRateId = @DefaultVatRateId,
            ItemGroupId = @ItemGroupId, PricePrecision = @PricePrecision, QuantityPrecision = @QuantityPrecision, IsStockable = @IsStockable,
            CnCode = @CnCode, DepositItemId = @DepositItemId, NetWeightKg = @NetWeightKg, GrossWeightKg = @GrossWeightKg, VolumeM3 = @VolumeM3,
            IsActive = @IsActive,
            OwnerCompanyId = CASE WHEN @SaveOwner = 1 THEN @OwnerCompanyId ELSE OwnerCompanyId END,
            OwnerBranchId = CASE WHEN @SaveOwner = 1 THEN @OwnerBranchId ELSE OwnerBranchId END,
            OwnerLocationId = CASE WHEN @SaveOwner = 1 THEN @OwnerLocationId ELSE OwnerLocationId END,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @Id)
                THROW 50027, N'Articolul nu există.', 1;
            THROW 50006, N'Articolul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    IF @SaveUoms = 1
    BEGIN
        DELETE FROM core.ItemUom WHERE ItemId = @Id;
        INSERT INTO core.ItemUom (ItemId, UomId, FactorToBase, IsDefaultSales, IsDefaultPurchase, IsDefaultPos)
        SELECT @Id, UomId, FactorToBase, IsDefaultSales, IsDefaultPurchase, IsDefaultPos FROM @Uoms;
    END;

    IF @SaveProperties = 1
    BEGIN
        DELETE v
        FROM core.PropertyValue AS v
        JOIN core.PropertyDefinition AS d ON d.Id = v.PropertyDefinitionId AND d.EntityType = 'Item'
        WHERE v.EntityId = @Id;

        INSERT INTO core.PropertyValue (PropertyDefinitionId, EntityId, ValueText, ValueNumber, ValueDate, ValueBool, ValueOptionId, ValueUserId, CreatedBy)
        SELECT p.PropertyDefinitionId, @Id, p.ValueText, p.ValueNumber, p.ValueDate, p.ValueBool, p.ValueOptionId, p.ValueUserId, @UserId
        FROM @Properties AS p;
    END;

    SELECT Id, RowVersion FROM core.Item WHERE Id = @Id;
END;
