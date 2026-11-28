CREATE OR ALTER PROCEDURE inv.usp_Lot_Update
    @CompanyId           int,
    @LotId               int,
    @SupplierLotCode     nvarchar(40) = NULL,
    @CountryOfOriginCode char(2) = NULL,
    @ProductionDate      date = NULL,
    @ExpiryDate          date = NULL,
    @Reason              nvarchar(400),
    @RowVersion          binary(8),
    @UserId              int
AS
BEGIN
    -- Atributele lotului (02.05, S4 4.4, Inv.Lot.Edit): lotul furnizorului, tara, fabricatia, termenul, cu motiv in istoric. CkuCode ramane cel de
    -- la creare; un lot Expirat cu termen nou in viitor nu revine singur la Disponibil. Acelasi cod cu termenul altui CKU: 54452.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Version binary(8), @Status tinyint, @Technical bit, @Variant int, @Code nvarchar(40);
    SELECT @Version = RowVersion, @Status = Status, @Technical = IsTechnical, @Variant = ItemVariantId, @Code = LotCode
    FROM inv.Lot WITH (UPDLOCK, ROWLOCK) WHERE Id = @LotId AND CompanyId = @CompanyId;
    IF @Version IS NULL OR @Technical = 1
        THROW 54454, N'Lotul nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 54014, N'Lotul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF EXISTS (SELECT 1 FROM inv.Lot WHERE CompanyId = @CompanyId AND ItemVariantId = @Variant AND LotCode = @Code AND Id <> @LotId
               AND ExpiryKey = ISNULL(@ExpiryDate, CONVERT(date, '19000101', 112)))
        THROW 54452, N'Există deja un lot cu același cod și același termen.', 1;

    UPDATE inv.Lot
    SET SupplierLotCode = NULLIF(LTRIM(RTRIM(@SupplierLotCode)), N''), CountryOfOriginCode = @CountryOfOriginCode, ProductionDate = @ProductionDate,
        ExpiryDate = @ExpiryDate, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @LotId;
    INSERT INTO inv.LotStatusHistory (LotId, FromStatus, ToStatus, Reason, ChangedBy) VALUES (@LotId, @Status, @Status, @Reason, @UserId);

    SELECT Id, RowVersion FROM inv.Lot WHERE Id = @LotId;
END;
