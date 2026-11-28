CREATE OR ALTER PROCEDURE inv.usp_RetailPriceChange_Save
    @Id           int = NULL,
    @CompanyId    int,
    @WarehouseId  int,
    @DocumentDate date,
    @Notes        nvarchar(1000) = NULL,
    @Lines        inv.tt_RetailPriceChangeLine READONLY,
    @RowVersion   binary(8) = NULL,
    @UserId       int
AS
BEGIN
    -- Ciorna documentului MP (02.05, S3 3.7): gestiunea la pret cu amanuntul a firmei (54312) si preturile noi pe variante. Liniile se inlocuiesc.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM inv.Warehouse WHERE Id = @WarehouseId AND CompanyId = @CompanyId AND ValuationBasis = 2)
        THROW 54312, N'Modificarea de preț se face doar pentru o gestiune la preț cu amănuntul a firmei.', 1;
    IF NOT EXISTS (SELECT 1 FROM @Lines)
        THROW 54312, N'Documentul de modificare de preț trebuie să aibă cel puțin o variantă.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS l LEFT JOIN core.ItemVariant AS v ON v.Id = l.ItemVariantId WHERE v.Id IS NULL OR l.NewPrice < 0)
        THROW 54312, N'O variantă nu există sau are prețul negativ.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO inv.RetailPriceChange (CompanyId, WarehouseId, DocumentDate, Notes, CreatedBy)
        VALUES (@CompanyId, @WarehouseId, @DocumentDate, @Notes, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        DECLARE @Status tinyint, @Version binary(8);
        SELECT @Status = Status, @Version = RowVersion FROM inv.RetailPriceChange WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
        IF @Status IS NULL
            THROW 54001, N'Documentul nu există în firma curentă.', 1;
        IF @RowVersion IS NULL OR @Version <> @RowVersion
            THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        IF @Status <> 1
            THROW 54118, N'Documentul nu mai este ciornă.', 1;

        UPDATE inv.RetailPriceChange
        SET WarehouseId = @WarehouseId, DocumentDate = @DocumentDate, Notes = @Notes, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id;
        DELETE FROM inv.RetailPriceChangeLine WHERE RetailPriceChangeId = @Id;
    END;

    INSERT INTO inv.RetailPriceChangeLine (RetailPriceChangeId, LineNumber, ItemVariantId, NewPrice)
    SELECT @Id, l.LineNumber, l.ItemVariantId, l.NewPrice FROM @Lines AS l;

    SELECT Id, RowVersion FROM inv.RetailPriceChange WHERE Id = @Id;
END;
