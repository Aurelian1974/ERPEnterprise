CREATE OR ALTER PROCEDURE inv.usp_RetailPriceChange_Validate
    @CompanyId  int,
    @Id         int,
    @RowVersion binary(8),
    @Number     nvarchar(40),
    @UserId     int
AS
BEGIN
    -- Validarea documentului MP (02.05, S3 3.7), in tranzactia apelantului: documentul si randurile de sold ale variantelor blocate (in ordinea
    -- cheii, SP-16); data documentului nu poate fi inaintea ultimei miscari a variantelor in gestiune (54313), ca miscarile deja facute sa ramana
    -- la pretul lor. Pe fiecare cheie, valoarea cu amanuntul devine cantitatea × pretul nou; diferenta (cu TVA-ul inclus) se scrie pe linie.
    -- Pretul nou intra in vigoare de la data documentului (inv.RetailPrice).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Version binary(8), @WarehouseId int, @Date date;
    SELECT @Status = Status, @Version = RowVersion, @WarehouseId = WarehouseId, @Date = DocumentDate
    FROM inv.RetailPriceChange WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Version <> @RowVersion
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status <> 1
        THROW 54118, N'Documentul nu mai este ciornă.', 1;

    DECLARE @Locked TABLE (StockId bigint PRIMARY KEY);
    INSERT INTO @Locked
    SELECT s.Id FROM inv.Stock AS s WITH (UPDLOCK, ROWLOCK)
    WHERE s.WarehouseId = @WarehouseId AND s.ItemVariantId IN (SELECT ItemVariantId FROM inv.RetailPriceChangeLine WHERE RetailPriceChangeId = @Id)
    ORDER BY s.WarehouseId, s.DepotKey, s.ItemVariantId, s.LotKey;

    IF EXISTS (SELECT 1 FROM inv.StockMovement AS m
               WHERE m.WarehouseId = @WarehouseId AND m.MovementDate > @Date
                 AND m.ItemVariantId IN (SELECT ItemVariantId FROM inv.RetailPriceChangeLine WHERE RetailPriceChangeId = @Id))
        THROW 54313, N'Există mișcări ale variantelor după data documentului; modificarea de preț se datează cel mai devreme în ziua ultimei mișcări.', 1;

    DECLARE @Keys TABLE (ItemVariantId int PRIMARY KEY, Qty decimal(19,6), Diff decimal(19,2));
    INSERT INTO @Keys
    SELECT s.ItemVariantId, SUM(s.QtyOnHand), SUM(ROUND(s.QtyOnHand * l.NewPrice, 2) - s.RetailValueOnHand)
    FROM inv.Stock AS s
    JOIN inv.RetailPriceChangeLine AS l ON l.ItemVariantId = s.ItemVariantId AND l.RetailPriceChangeId = @Id
    WHERE s.WarehouseId = @WarehouseId
    GROUP BY s.ItemVariantId;

    UPDATE l
    SET OldPrice = p.Price, VatPercent = vr.[Percent], Qty = ISNULL(k.Qty, 0), DiffAmount = ISNULL(k.Diff, 0),
        DiffVatAmount = ROUND(ISNULL(k.Diff, 0) * vr.[Percent] / (100 + vr.[Percent]), 2)
    FROM inv.RetailPriceChangeLine AS l
    JOIN core.ItemVariant AS v ON v.Id = l.ItemVariantId
    JOIN core.Item AS i ON i.Id = v.ItemId
    JOIN core.VatRate AS vr ON vr.Id = i.DefaultVatRateId
    OUTER APPLY (SELECT TOP (1) x.Price FROM inv.RetailPrice AS x
                 WHERE x.WarehouseId = @WarehouseId AND x.ItemVariantId = l.ItemVariantId AND x.ValidFrom <= @Date ORDER BY x.ValidFrom DESC) AS p
    LEFT JOIN @Keys AS k ON k.ItemVariantId = l.ItemVariantId
    WHERE l.RetailPriceChangeId = @Id;

    UPDATE s SET RetailValueOnHand = ROUND(s.QtyOnHand * l.NewPrice, 2)
    FROM inv.Stock AS s
    JOIN inv.RetailPriceChangeLine AS l ON l.ItemVariantId = s.ItemVariantId AND l.RetailPriceChangeId = @Id
    WHERE s.WarehouseId = @WarehouseId;

    MERGE inv.RetailPrice AS target
    USING (SELECT ItemVariantId, NewPrice FROM inv.RetailPriceChangeLine WHERE RetailPriceChangeId = @Id) AS source
    ON target.WarehouseId = @WarehouseId AND target.ItemVariantId = source.ItemVariantId AND target.ValidFrom = @Date
    WHEN MATCHED THEN UPDATE SET Price = source.NewPrice, RetailPriceChangeId = @Id
    WHEN NOT MATCHED THEN INSERT (CompanyId, WarehouseId, ItemVariantId, Price, ValidFrom, RetailPriceChangeId, CreatedBy)
        VALUES (@CompanyId, @WarehouseId, source.ItemVariantId, source.NewPrice, @Date, @Id, @UserId);

    UPDATE inv.RetailPriceChange
    SET Status = 2, Number = @Number, ValidatedAt = SYSUTCDATETIME(), ValidatedBy = @UserId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId,
        DiffAmount = (SELECT SUM(DiffAmount) FROM inv.RetailPriceChangeLine WHERE RetailPriceChangeId = @Id),
        DiffVatAmount = (SELECT SUM(DiffVatAmount) FROM inv.RetailPriceChangeLine WHERE RetailPriceChangeId = @Id)
    WHERE Id = @Id;

    SELECT Id, RowVersion FROM inv.RetailPriceChange WHERE Id = @Id;
END;
