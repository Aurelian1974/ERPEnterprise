CREATE OR ALTER PROCEDURE inv.usp_Lot_ResolveForMovements
    @CompanyId           int,
    @SourceModule        varchar(20),
    @MovementDate        date,
    @IsExternalFact      bit = 0,
    @PartnerId           int = NULL,
    @AllowExpiredReceipt bit = 0,
    @Lines               inv.tt_LotRequest READONLY,
    @UserId              int
AS
BEGIN
    -- Loturile unui apel al portului de stoc (02.05, S4 3.2, 3.5, 3.7), in tranzactia apelantului, inaintea costului:
    --   1. reluarea: daca toate liniile sursei sunt deja inregistrate, intoarce miscarile existente (lotul si cantitatea fiecareia);
    --   2. intrarile modelelor urmarite pe lot: lotul dat (Id sau CkuUid), gasit dupa cod si termen sau creat (CKU nou: carantina la receptie,
    --      expirat doar cu dreptul), codul generat la LotNumbering = 2, lotul tehnic NEIDENTIFICAT pentru faptele din afara ERP;
    --   3. iesirile: lotul dat se verifica; celelalte se aloca de apelant (LotAllocator, FEFO) din loturile cheii, cu disponibilul la data
    --      miscarii si la toate datele ulterioare; pentru faptele din afara ERP, lotul tehnic primeste ce nu ajunge.
    -- Coduri: 54451, 54452, 54453, 54454, 54457.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- 1. Reluarea.
    DECLARE @Total int = (SELECT COUNT(*) FROM @Lines);
    DECLARE @Existing int = (SELECT COUNT(*) FROM @Lines AS l WHERE EXISTS (SELECT 1 FROM inv.StockMovement AS m
        WHERE m.SourceModule = @SourceModule AND m.SourceLineId = l.SourceLineId AND m.SourceRole = l.SourceRole AND m.ReversalKey = ISNULL(l.ReversalOfId, 0)));
    SELECT CAST(CASE WHEN @Total > 0 AND @Existing = @Total THEN 1 ELSE 0 END AS bit) AS Replayed;
    IF @Total > 0 AND @Existing = @Total
    BEGIN
        SELECT l.RowNo, m.LotId, m.QtyBase
        FROM @Lines AS l
        JOIN inv.StockMovement AS m ON m.SourceModule = @SourceModule AND m.SourceLineId = l.SourceLineId AND m.SourceRole = l.SourceRole
            AND m.ReversalKey = ISNULL(l.ReversalOfId, 0)
        ORDER BY l.RowNo, m.PostingSeq;
        RETURN;
    END;

    DECLARE @Rows TABLE (
        RowNo int PRIMARY KEY, ItemVariantId int, VariantCode nvarchar(60) COLLATE DATABASE_DEFAULT, WarehouseId int, DepotId int, QtyBase decimal(19,6),
        MovementTypeCode varchar(30) COLLATE DATABASE_DEFAULT, Tracking tinyint, ExpiryKind tinyint, ShelfLifeDays int, Quarantine bit, Numbering tinyint,
        LotId int, LotCode nvarchar(40) COLLATE DATABASE_DEFAULT, ExpiryDate date, ProductionDate date, SupplierLotCode nvarchar(40) COLLATE DATABASE_DEFAULT,
        CountryCode char(2) COLLATE DATABASE_DEFAULT, ConfirmNewExpiry bit, Kind tinyint);   -- Kind: 0 fara lot, 1 intrare, 2 iesire, 3 pereche sau stornare
    INSERT INTO @Rows
    SELECT l.RowNo, l.ItemVariantId, v.Code, l.WarehouseId, l.DepotId, l.QtyBase, l.MovementTypeCode, ISNULL(s.TrackingMode, 0), s.ExpiryKind, s.ShelfLifeDays,
        ISNULL(s.ReceiptQuarantine, 0), ISNULL(s.LotNumbering, 1), l.LotId, NULLIF(LTRIM(RTRIM(l.NewLotCode)), N''), l.ExpiryDate, l.ProductionDate,
        NULLIF(LTRIM(RTRIM(l.SupplierLotCode)), N''), l.CountryOfOriginCode, l.ConfirmNewExpiry,
        CASE WHEN ISNULL(s.TrackingMode, 0) NOT IN (1, 2, 4) THEN 0
             WHEN l.IsCounter = 1 OR l.ReversalOfId IS NOT NULL THEN 3
             WHEN l.QtyBase > 0 THEN 1 ELSE 2 END
    FROM @Lines AS l
    JOIN core.ItemVariant AS v ON v.Id = l.ItemVariantId
    LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = @CompanyId AND s.ItemId = v.ItemId;

    UPDATE @Rows SET LotId = NULL WHERE Kind = 0;

    -- Lotul dat prin CkuUid (eticheta interna).
    UPDATE r SET LotId = x.Id
    FROM @Rows AS r JOIN @Lines AS l ON l.RowNo = r.RowNo
    JOIN inv.Lot AS x ON x.CkuUid = l.CkuUid
    WHERE r.Kind <> 0 AND r.LotId IS NULL AND l.CkuUid IS NOT NULL;

    -- Faptele din afara ERP (POS) cu un lot necunoscut se aloca FEFO, ca si cele fara lot (S4 3.5).
    IF @IsExternalFact = 1
        UPDATE r SET LotId = NULL
        FROM @Rows AS r LEFT JOIN inv.Lot AS x ON x.Id = r.LotId
        WHERE r.Kind = 2 AND r.LotId IS NOT NULL AND (x.Id IS NULL OR x.CompanyId <> @CompanyId OR x.ItemVariantId <> r.ItemVariantId);

    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN @Lines AS l ON l.RowNo = r.RowNo WHERE r.Kind <> 0 AND r.LotId IS NULL AND l.CkuUid IS NOT NULL
               AND NOT (@IsExternalFact = 1 AND r.Kind = 2))
       OR EXISTS (SELECT 1 FROM @Rows AS r LEFT JOIN inv.Lot AS x ON x.Id = r.LotId
                  WHERE r.Kind <> 0 AND r.LotId IS NOT NULL AND (x.Id IS NULL OR x.CompanyId <> @CompanyId OR x.ItemVariantId <> r.ItemVariantId))
        THROW 54454, N'Lotul liniei nu există sau este al altei variante ori firme.', 1;

    -- 2. Intrarile fara lot: codul generat (LotNumbering = 2), lotul tehnic pentru faptele din afara ERP, altfel lotul e obligatoriu.
    WITH Numbered AS (
        SELECT RowNo, ROW_NUMBER() OVER (PARTITION BY ItemVariantId ORDER BY RowNo) AS N
        FROM @Rows WHERE Kind = 1 AND LotId IS NULL AND LotCode IS NULL AND Numbering = 2
    )
    UPDATE r SET LotCode = CONCAT(N'L', FORMAT(@MovementDate, 'yyMMdd'), N'-',
        (SELECT COUNT(*) FROM inv.Lot AS x WHERE x.CompanyId = @CompanyId AND x.ItemVariantId = r.ItemVariantId) + n.N)
    FROM @Rows AS r
    JOIN Numbered AS n ON n.RowNo = r.RowNo;

    IF EXISTS (SELECT 1 FROM @Rows WHERE Kind = 1 AND LotId IS NULL AND LotCode IS NULL) AND @IsExternalFact = 0
        THROW 54453, N'Articolul este urmărit pe lot: intrarea cere lotul.', 1;

    -- Termenul: dat, altfel fabricatia (sau data intrarii) + durata de valabilitate; doar la modelele cu lot si termen.
    UPDATE @Rows SET ExpiryDate = COALESCE(ExpiryDate, DATEADD(DAY, ShelfLifeDays, COALESCE(ProductionDate, CASE WHEN Numbering = 2 THEN @MovementDate END)))
    WHERE Kind = 1 AND LotId IS NULL AND LotCode IS NOT NULL AND Tracking = 2;
    UPDATE @Rows SET ExpiryDate = NULL WHERE Kind = 1 AND LotId IS NULL AND Tracking <> 2 AND Tracking <> 4;
    IF EXISTS (SELECT 1 FROM @Rows WHERE Kind = 1 AND LotId IS NULL AND LotCode IS NOT NULL AND Tracking = 2 AND ExpiryDate IS NULL)
        THROW 54451, N'Articolul are lot și termen de valabilitate: intrarea cere termenul.', 1;

    -- Acelasi cod de lot cu alt termen cere confirmarea (de obicei e o greseala de introducere).
    IF EXISTS (SELECT 1 FROM @Rows AS r
               WHERE r.Kind = 1 AND r.LotId IS NULL AND r.LotCode IS NOT NULL AND r.ConfirmNewExpiry = 0
                 AND NOT EXISTS (SELECT 1 FROM inv.Lot AS x WHERE x.CompanyId = @CompanyId AND x.ItemVariantId = r.ItemVariantId AND x.LotCode = r.LotCode
                                 AND x.ExpiryKey = ISNULL(r.ExpiryDate, CONVERT(date, '19000101', 112)))
                 AND EXISTS (SELECT 1 FROM inv.Lot AS x WHERE x.CompanyId = @CompanyId AND x.ItemVariantId = r.ItemVariantId AND x.LotCode = r.LotCode))
        THROW 54452, N'Lotul există deja cu alt termen; confirmați crearea unui lot nou cu același cod.', 1;

    IF @AllowExpiredReceipt = 0 AND EXISTS (SELECT 1 FROM @Rows WHERE Kind = 1 AND LotId IS NULL AND LotCode IS NOT NULL AND Tracking = 2
                                                AND ExpiryKind = 1 AND ExpiryDate < @MovementDate)
        THROW 54457, N'Termenul lotului a trecut; recepția unui lot expirat cere dreptul Inv.Lot.ReceiveExpired.', 1;

    -- Loturile noi (CKU): unul pe varianta, cod si termen.
    DECLARE @New TABLE (Id int, ItemVariantId int, LotCode nvarchar(40) COLLATE DATABASE_DEFAULT, ExpiryKey date, Status tinyint);
    INSERT INTO inv.Lot (CompanyId, ItemVariantId, LotCode, CkuCode, SupplierLotCode, SupplierPartnerId, ProductionDate, ExpiryDate, ExpiryKind,
        CountryOfOriginCode, Status, FirstReceiptDate, CreatedBy)
    OUTPUT inserted.Id, inserted.ItemVariantId, inserted.LotCode, inserted.ExpiryKey, inserted.Status INTO @New
    SELECT @CompanyId, n.ItemVariantId, n.LotCode,
        CONCAT(n.VariantCode, N'·', n.LotCode, CASE WHEN n.ExpiryDate IS NOT NULL THEN N'·' + FORMAT(n.ExpiryDate, 'yyMMdd') END),
        n.SupplierLotCode, @PartnerId, n.ProductionDate, n.ExpiryDate, CASE WHEN n.Tracking IN (2, 4) AND n.ExpiryDate IS NOT NULL THEN n.ExpiryKind END,
        n.CountryCode,
        CASE WHEN n.Quarantine = 1 THEN 2 WHEN n.Tracking = 2 AND n.ExpiryKind = 1 AND n.ExpiryDate < @MovementDate THEN 4 ELSE 1 END,
        @MovementDate, @UserId
    FROM (SELECT r.*, ROW_NUMBER() OVER (PARTITION BY r.ItemVariantId, r.LotCode, ISNULL(r.ExpiryDate, '19000101') ORDER BY r.RowNo) AS Pick
          FROM @Rows AS r WHERE r.Kind = 1 AND r.LotId IS NULL AND r.LotCode IS NOT NULL) AS n
    WHERE n.Pick = 1
      AND NOT EXISTS (SELECT 1 FROM inv.Lot AS x WITH (UPDLOCK, HOLDLOCK) WHERE x.CompanyId = @CompanyId AND x.ItemVariantId = n.ItemVariantId
                      AND x.LotCode = n.LotCode AND x.ExpiryKey = ISNULL(n.ExpiryDate, CONVERT(date, '19000101', 112)));

    INSERT INTO inv.LotStatusHistory (LotId, FromStatus, ToStatus, Reason, SourceDocumentType, ChangedBy)
    SELECT Id, NULL, Status, N'Creare la intrare', @SourceModule, @UserId FROM @New;

    UPDATE r SET LotId = x.Id
    FROM @Rows AS r
    JOIN inv.Lot AS x ON x.CompanyId = @CompanyId AND x.ItemVariantId = r.ItemVariantId AND x.LotCode = r.LotCode
        AND x.ExpiryKey = ISNULL(r.ExpiryDate, CONVERT(date, '19000101', 112)) AND x.IsTechnical = 0
    WHERE r.Kind = 1 AND r.LotId IS NULL AND r.LotCode IS NOT NULL;

    -- Lotul tehnic NEIDENTIFICAT (S4 3.7), doar pentru faptele din afara ERP: intrarile fara lot si iesirile pe care loturile nu le acopera.
    IF @IsExternalFact = 1
    BEGIN
        INSERT INTO inv.Lot (CompanyId, ItemVariantId, LotCode, CkuCode, IsTechnical, Status, CreatedBy)
        SELECT DISTINCT @CompanyId, r.ItemVariantId, N'NEIDENTIFICAT', CONCAT(r.VariantCode, N'·NEIDENTIFICAT'), 1, 1, @UserId
        FROM @Rows AS r
        WHERE r.Kind IN (1, 2) AND r.LotId IS NULL
          AND NOT EXISTS (SELECT 1 FROM inv.Lot AS x WITH (UPDLOCK, HOLDLOCK) WHERE x.CompanyId = @CompanyId AND x.ItemVariantId = r.ItemVariantId
                          AND x.IsTechnical = 1);

        UPDATE r SET LotId = x.Id
        FROM @Rows AS r JOIN inv.Lot AS x ON x.CompanyId = @CompanyId AND x.ItemVariantId = r.ItemVariantId AND x.IsTechnical = 1
        WHERE r.Kind = 1 AND r.LotId IS NULL;
    END;

    -- Durata minima ramasa la livrarea catre client (S4 3.2): regula clientului pe model, apoi pe grupa, apoi setarea modelului.
    SELECT r.RowNo, r.Kind, r.LotId,
        CASE WHEN r.Kind = 2 AND r.MovementTypeCode IN ('ISSUE_SALE', 'POS_SALE')
             THEN COALESCE(pi.MinRemainingDays, pg.MinRemainingDays, s.MinRemainingShelfLifeDays, 0) ELSE 0 END AS MinRemainingDays
    FROM @Rows AS r
    JOIN core.ItemVariant AS v ON v.Id = r.ItemVariantId
    JOIN core.Item AS i ON i.Id = v.ItemId
    LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = @CompanyId AND s.ItemId = i.Id
    LEFT JOIN inv.PartnerShelfLifeRule AS pi ON pi.CompanyId = @CompanyId AND pi.PartnerId = @PartnerId AND pi.ItemId = i.Id
    LEFT JOIN inv.PartnerShelfLifeRule AS pg ON pg.CompanyId = @CompanyId AND pg.PartnerId = @PartnerId AND pg.ItemGroupId = i.ItemGroupId
    ORDER BY r.RowNo;

    -- 3. Loturile cheilor cu iesiri: starea, termenul, prima intrare si disponibilul (soldul la data miscarii si minimul la datele ulterioare).
    DECLARE @Keys TABLE (WarehouseId int, DepotKey int, ItemVariantId int, PRIMARY KEY (WarehouseId, DepotKey, ItemVariantId));
    INSERT INTO @Keys SELECT DISTINCT WarehouseId, ISNULL(DepotId, 0), ItemVariantId FROM @Rows WHERE Kind = 2;

    SELECT k.WarehouseId, NULLIF(k.DepotKey, 0) AS DepotId, k.ItemVariantId, x.Id AS LotId, x.CkuCode, x.LotCode, x.Status, x.ExpiryDate, x.ExpiryKind,
        x.FirstReceiptDate, x.IsTechnical, a.Available
    FROM @Keys AS k
    CROSS APPLY inv.ufn_LotAvailability(k.WarehouseId, k.DepotKey, k.ItemVariantId, @MovementDate) AS a
    JOIN inv.Lot AS x ON x.Id = a.LotId;

    -- Lotul tehnic al variantelor cu iesiri din afara ERP.
    SELECT x.ItemVariantId, x.Id AS LotId
    FROM inv.Lot AS x
    WHERE @IsExternalFact = 1 AND x.CompanyId = @CompanyId AND x.IsTechnical = 1 AND x.ItemVariantId IN (SELECT ItemVariantId FROM @Rows WHERE Kind = 2);

    SELECT CAST(ISNULL((SELECT ExpiryPickRule FROM inv.StockSettings WHERE CompanyId = @CompanyId), 1) AS tinyint) AS PickRule;
END;
