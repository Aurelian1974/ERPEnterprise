CREATE OR ALTER PROCEDURE core.usp_ItemBarcode_Save
    @Id          int = NULL,
    @ItemId      int,
    @Barcode     varchar(50),
    @BarcodeType tinyint,
    @UomId       int = NULL,
    @VariantId   int = NULL,
    @IsActive    bit,
    @RowVersion  binary(8) = NULL,
    @UserId      int
AS
BEGIN
    -- Codul de bare al articolului (02.01, 3.3): unic in instalare; UM aleasa e una a articolului (din set sau exceptie).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Item WHERE Id = @ItemId)
        THROW 50027, N'Articolul nu există.', 1;

    IF @UomId IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM core.Item AS i JOIN core.UomSetLine AS l ON l.UomSetId = i.UomSetId WHERE i.Id = @ItemId AND l.UomId = @UomId
        UNION ALL
        SELECT 1 FROM core.ItemUom AS x WHERE x.ItemId = @ItemId AND x.UomId = @UomId)
        THROW 50021, N'UM aleasă nu e una a articolului.', 1;

    IF @VariantId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.ItemVariant WHERE Id = @VariantId AND ItemId = @ItemId)
        THROW 50021, N'Varianta aleasă nu e a articolului.', 1;

    IF EXISTS (SELECT 1 FROM core.ItemBarcode WITH (UPDLOCK, HOLDLOCK) WHERE Barcode = @Barcode AND (@Id IS NULL OR Id <> @Id))
        THROW 50005, N'Codul de bare e folosit la alt articol.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.ItemBarcode (ItemId, Barcode, BarcodeType, UomId, VariantId, IsActive, CreatedBy)
        VALUES (@ItemId, @Barcode, @BarcodeType, @UomId, @VariantId, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.ItemBarcode
        SET Barcode = @Barcode, BarcodeType = @BarcodeType, UomId = @UomId, VariantId = @VariantId, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND ItemId = @ItemId AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.ItemBarcode WHERE Id = @Id AND ItemId = @ItemId)
                THROW 50027, N'Codul de bare nu există la acest articol.', 1;
            THROW 50006, N'Codul de bare a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.ItemBarcode WHERE Id = @Id;
END;
