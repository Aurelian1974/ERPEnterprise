CREATE OR ALTER PROCEDURE core.usp_VatRate_SaveBatch
    @Rows   core.VatRateList READONLY,
    @UserId int
AS
BEGIN
    -- Cotele TVA editate in tabel (macheta 07), salvate impreuna (P6). O cota folosita nu se modifica: i se poate pune doar
    -- „valabil pana la”, iar o schimbare de lege se face cu o cota noua. Perioadele aceluiasi cod nu se suprapun.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN core.VatRate AS v WITH (UPDLOCK, HOLDLOCK) ON v.Id = r.Id WHERE v.RowVersion <> r.RowVersion OR r.RowVersion IS NULL)
        THROW 50006, N'O cotă TVA a fost modificată de alt utilizator; reîncărcați datele.', 1;

    IF EXISTS (SELECT 1 FROM @Rows AS r WHERE r.Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.VatRate AS v WHERE v.Id = r.Id))
        THROW 50027, N'O cotă TVA nu există.', 1;

    IF EXISTS (
        SELECT 1 FROM @Rows AS r JOIN core.VatRate AS v ON v.Id = r.Id
        WHERE EXISTS (SELECT 1 FROM core.Item AS i WHERE i.DefaultVatRateId = v.Id)
          AND (r.Code <> v.Code OR r.[Percent] <> v.[Percent] OR r.Category <> v.Category OR r.ValidFrom <> v.ValidFrom))
        THROW 50010, N'O cotă folosită nu se modifică; adăugați o cotă nouă cu „Valabil de la” și închideți-o pe cea veche.', 1;

    UPDATE v
    SET Code = r.Code, Name = r.Name, [Percent] = r.[Percent], Category = r.Category, ValidFrom = r.ValidFrom, ValidTo = r.ValidTo,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM core.VatRate AS v
    JOIN @Rows AS r ON r.Id = v.Id;

    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN core.VatRate AS v ON v.Code = r.Code AND v.ValidFrom = r.ValidFrom WHERE r.Id IS NULL)
        THROW 50018, N'Există deja o cotă cu același cod și aceeași dată de început.', 1;

    INSERT INTO core.VatRate (Code, Name, [Percent], Category, ValidFrom, ValidTo, CreatedBy)
    SELECT r.Code, r.Name, r.[Percent], r.Category, r.ValidFrom, r.ValidTo, @UserId
    FROM @Rows AS r
    WHERE r.Id IS NULL;

    IF EXISTS (
        SELECT 1 FROM core.VatRate AS a
        JOIN core.VatRate AS b ON b.Code = a.Code AND b.Id > a.Id
        WHERE a.ValidFrom <= COALESCE(b.ValidTo, '9999-12-31') AND b.ValidFrom <= COALESCE(a.ValidTo, '9999-12-31'))
        THROW 50021, N'Perioadele de valabilitate ale aceluiași cod de cotă se suprapun.', 1;

    SELECT COUNT(*) FROM @Rows;
END;
