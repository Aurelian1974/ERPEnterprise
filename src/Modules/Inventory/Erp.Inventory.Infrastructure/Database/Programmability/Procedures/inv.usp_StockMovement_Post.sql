CREATE OR ALTER PROCEDURE inv.usp_StockMovement_Post
    @CompanyId            int,
    @SourceModule         varchar(20),
    @SourceDocumentType   varchar(30),
    @SourceDocumentId     bigint,
    @SourceDocumentNumber nvarchar(40) = NULL,
    @MovementDate         date,
    @AccountingDate       date,
    @PartnerId            int = NULL,
    @EmployeeId           int = NULL,
    @IsExternalFact       bit = 0,
    @Lines                inv.tt_StockMovementCosted READONLY,
    @KeyStates            inv.tt_CostKeyState READONLY,
    @Layers               inv.tt_CostLayerWrite READONLY,
    @Consumptions         inv.tt_CostConsumptionWrite READONLY,
    @Adjustments          inv.tt_CostAdjustmentWrite READONLY,
    @UserId               int
AS
BEGIN
    -- Inregistrarea unui set de miscari (02.05, S2 4.1; portul IStockLedger), in tranzactia apelantului:
    --   1. verificarile pe linii (gestiunea firmei, activa si neblocata, depozitul, tipul si semnul, varianta stocabila, tranzitul si custodia);
    --   2. idempotenta: daca toate liniile sursei sunt deja inregistrate se intorc miscarile existente; doar unele: 54102 (S2 regula 6);
    --   3. blocarea randurilor de sold atinse (UPDLOCK, in ordinea cheii, SP-16), scrierea miscarilor cu costul calculat de motorul de cost
    --      (CostEngine, S3), a straturilor, consumurilor si regularizarilor, a soldului si a soldurilor pe zile;
    --   4. stocul negativ verificat la data miscarii si la toate datele ulterioare, din soldurile pe zile (54101, S2 regula 5); politica
    --      permisiva si faptele din afara ERP trec, cu marcajul IsForcedNegative.
    -- Coduri: 54001, 54005, 54017, 54101, 54102, 54104, 54105, 54108, 54110, 54116, 54311, 54011.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Rows TABLE (
        RowNo int PRIMARY KEY, SourceLineId bigint, SourceRole tinyint, MovementTypeId smallint, Direction smallint, DepotId int, WarehouseId int,
        ItemVariantId int, QtyBase decimal(19,6), LotId int, SerialId int, CostCenterId int, ProjectId int, ReasonId int,
        CounterRowNo int, ReversalOfId bigint, LotOverrideReason tinyint, NegativePolicy tinyint, UnitCost decimal(19,6), CostAmount decimal(19,2),
        IsForcedNegative bit NOT NULL DEFAULT (0), MovementId bigint, PostingSeq bigint, RetailPrice decimal(19,6), RetailAmount decimal(19,2),
        RetailVatAmount decimal(19,2), RetailDischargeMethod tinyint);

    INSERT INTO @Rows (RowNo, SourceLineId, SourceRole, MovementTypeId, Direction, DepotId, WarehouseId, ItemVariantId, QtyBase, LotId,
        SerialId, CostCenterId, ProjectId, ReasonId, CounterRowNo, ReversalOfId, LotOverrideReason, UnitCost, CostAmount)
    SELECT l.RowNo, l.SourceLineId, l.SourceRole, t.Id, SIGN(l.QtyBase), l.DepotId, l.WarehouseId, l.ItemVariantId, l.QtyBase, l.LotId,
        l.SerialId, l.CostCenterId, l.ProjectId, l.ReasonId, l.CounterRowNo, l.ReversalOfId, l.LotOverrideReason, l.UnitCost, l.CostAmount
    FROM @Lines AS l
    LEFT JOIN inv.MovementType AS t ON t.Code = l.MovementTypeCode;

    IF NOT EXISTS (SELECT 1 FROM @Rows)
        RETURN;

    -- 2. Idempotenta pe sursa.
    DECLARE @Existing int = (SELECT COUNT(*) FROM @Rows AS r JOIN inv.StockMovement AS m
                             ON m.SourceModule = @SourceModule AND m.SourceLineId = r.SourceLineId AND m.SourceRole = r.SourceRole
                                AND m.ReversalKey = ISNULL(r.ReversalOfId, 0) AND m.LotKey = ISNULL(r.LotId, 0) AND m.SerialKey = ISNULL(r.SerialId, 0));
    IF @Existing = (SELECT COUNT(*) FROM @Rows)
    BEGIN
        SELECT m.Id AS MovementId, m.SourceLineId, m.SourceRole, m.WarehouseId, m.DepotId, m.ItemVariantId, m.QtyBase, m.UnitCost, m.CostAmount,
            m.IsForcedNegative, t.Code AS MovementTypeCode, mr.Code AS ReasonCode, CAST(1 AS bit) AS Replayed, m.RetailAmount, m.RetailVatAmount,
            m.RetailDischargeMethod, COALESCE(m.CostCenterId, w.DefaultCostCenterId) AS CostCenterId, m.LotId, lot.LotCode, lot.CkuCode, lot.CkuUid,
            lot.ExpiryDate, m.SerialId, ser.SerialNo, ser.WarrantyUntil
        FROM @Rows AS r
        JOIN inv.StockMovement AS m ON m.SourceModule = @SourceModule AND m.SourceLineId = r.SourceLineId AND m.SourceRole = r.SourceRole
            AND m.ReversalKey = ISNULL(r.ReversalOfId, 0) AND m.LotKey = ISNULL(r.LotId, 0) AND m.SerialKey = ISNULL(r.SerialId, 0)
        JOIN inv.MovementType AS t ON t.Id = m.MovementTypeId
        LEFT JOIN inv.Lot AS lot ON lot.Id = m.LotId
        LEFT JOIN inv.Serial AS ser ON ser.Id = m.SerialId
        JOIN inv.Warehouse AS w ON w.Id = m.WarehouseId
        LEFT JOIN inv.StockSettings AS ss ON ss.CompanyId = @CompanyId
        LEFT JOIN inv.MovementReason AS mr ON mr.Id = m.ReasonId
        ORDER BY r.RowNo;
        RETURN;
    END;
    IF @Existing > 0
        THROW 54102, N'Unele linii ale documentului sursă au deja mișcări de stoc.', 1;

    -- 1. Verificarile pe linii.
    IF EXISTS (SELECT 1 FROM @Rows WHERE MovementTypeId IS NULL OR QtyBase = 0)
       OR EXISTS (SELECT 1 FROM @Rows AS r JOIN inv.MovementType AS t ON t.Id = r.MovementTypeId WHERE t.Direction <> r.Direction AND r.ReversalOfId IS NULL)
        THROW 54104, N'Cantitatea unei linii este zero sau are semnul greșit pentru tipul mișcării.', 1;
    IF EXISTS (SELECT 1 FROM @Rows AS r LEFT JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId AND w.CompanyId = @CompanyId
               WHERE w.Id IS NULL OR (w.IsActive = 0 AND r.ReversalOfId IS NULL))
        THROW 54001, N'Gestiunea unei linii nu există în firmă sau este inactivă.', 1;
    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN core.ItemVariant AS v ON v.Id = r.ItemVariantId JOIN core.Item AS i ON i.Id = v.ItemId
               WHERE (v.IsActive = 0 OR i.IsStockable = 0) AND r.ReversalOfId IS NULL)
       OR EXISTS (SELECT 1 FROM @Rows AS r WHERE NOT EXISTS (SELECT 1 FROM core.ItemVariant AS v WHERE v.Id = r.ItemVariantId))
        THROW 54105, N'Varianta unei linii este inactivă sau articolul nu este stocabil.', 1;
    IF @IsExternalFact = 0 AND EXISTS (
        SELECT 1 FROM @Rows AS r JOIN inv.MovementType AS t ON t.Id = r.MovementTypeId JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId
        LEFT JOIN inv.Depot AS d ON d.Id = r.DepotId
        WHERE (w.IsBlocked = 1 OR d.IsBlocked = 1) AND t.Code <> 'TRANSFER_IN')
        THROW 54005, N'Gestiunea sau depozitul unei linii este blocat (inventar, predare sau blocare manuală).', 1;
    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN inv.MovementType AS t ON t.Id = r.MovementTypeId JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId
               WHERE w.Kind = 3 AND t.Code NOT IN ('TRANSFER_OUT', 'TRANSFER_IN', 'UNLOAD', 'LOAD'))
        THROW 54108, N'Gestiunea de tranzit primește doar transferurile în două etape și rezolvarea diferențelor.', 1;
    IF EXISTS (SELECT 1 FROM @Rows AS r JOIN inv.MovementType AS t ON t.Id = r.MovementTypeId JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId
               WHERE (w.Kind = 4 AND t.Code NOT IN ('CUSTODY_IN', 'CUSTODY_OUT', 'TRANSFER_OUT', 'TRANSFER_IN', 'COUNT_PLUS', 'COUNT_MINUS'))
                  OR (w.Kind <> 4 AND t.Code IN ('CUSTODY_IN', 'CUSTODY_OUT')))
        THROW 54116, N'Custodia are doar intrări și ieșiri de custodie; din ea nu se vinde și nu se consumă.', 1;
    -- Nivelul „depozit” (S1 3.8): gestiunea fara locatie dintr-un depozit cere depozitul liniei; depozitul trebuie sa contina gestiunea.
    IF EXISTS (SELECT 1 FROM @Rows AS r WHERE r.DepotId IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM inv.DepotWarehouse AS dw JOIN inv.Depot AS d ON d.Id = dw.DepotId
                               WHERE dw.DepotId = r.DepotId AND dw.WarehouseId = r.WarehouseId AND d.CompanyId = @CompanyId))
       OR EXISTS (SELECT 1 FROM @Rows AS r JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId
                  WHERE r.DepotId IS NULL AND w.LocationId IS NULL AND w.Kind IN (1, 2, 4)
                    AND EXISTS (SELECT 1 FROM inv.DepotWarehouse AS dw WHERE dw.WarehouseId = w.Id AND dw.IsActive = 1))
        THROW 54017, N'La nivelul „depozit” linia cere depozitul care conține gestiunea.', 1;

    IF EXISTS (SELECT 1 FROM @Rows AS r LEFT JOIN inv.MovementReason AS mr ON mr.Id = r.ReasonId
               WHERE r.ReasonId IS NOT NULL AND r.ReversalOfId IS NULL
                 AND (mr.Id IS NULL OR mr.Direction <> r.Direction OR (mr.CompanyId IS NOT NULL AND mr.CompanyId <> @CompanyId)))
        THROW 54110, N'Motivul liniei nu există în firmă sau are direcția greșită.', 1;

    -- Schimbarea metodei de evaluare (S1 4.5): nicio miscare cu data inaintea schimbarii validate a modelului (54011).
    IF @IsExternalFact = 0 AND EXISTS (
        SELECT 1 FROM @Rows AS r
        JOIN core.ItemVariant AS v ON v.Id = r.ItemVariantId
        JOIN inv.ValuationMethodChangeLine AS l ON l.ItemId = v.ItemId
        JOIN inv.ValuationMethodChange AS c ON c.Id = l.ChangeId AND c.CompanyId = @CompanyId AND c.Status = 2
        WHERE @MovementDate < c.EffectiveDate AND r.ReversalOfId IS NULL)
        THROW 54011, N'Metoda de evaluare a articolului s-a schimbat după data mișcării; mișcarea nu se poate data înaintea schimbării.', 1;

    -- Politica de stoc negativ (S1 3.6): loturile si seriile, tranzitul si custodia sunt mereu blocate; altfel politica gestiunii.
    UPDATE r
    SET NegativePolicy = CASE WHEN w.Kind IN (3, 4) OR ISNULL(s.TrackingMode, 0) <> 0 THEN 1 ELSE w.NegativeStockPolicy END
    FROM @Rows AS r
    JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId
    JOIN core.ItemVariant AS v ON v.Id = r.ItemVariantId
    LEFT JOIN inv.ItemStockSettings AS s ON s.CompanyId = @CompanyId AND s.ItemId = v.ItemId;

    -- Gestiunile la pret cu amanuntul (S3 3.7): pretul in vigoare la data miscarii pe gestiune si varianta, valoarea cu amanuntul si TVA-ul
    -- inclus (cota articolului); stornarea ia valorile originalului, cu semn opus. Fara pret in vigoare: 54311.
    UPDATE r
    SET RetailDischargeMethod = COALESCE(w.RetailDischargeMethod, ss.RetailDischargeMethod, 1), RetailPrice = p.Price,
        RetailAmount = ROUND(r.QtyBase * p.Price, 2),
        RetailVatAmount = ROUND(ROUND(r.QtyBase * p.Price, 2) * vr.[Percent] / (100 + vr.[Percent]), 2)
    FROM @Rows AS r
    JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId AND w.ValuationBasis = 2
    LEFT JOIN inv.StockSettings AS ss ON ss.CompanyId = @CompanyId
    JOIN core.ItemVariant AS v ON v.Id = r.ItemVariantId
    JOIN core.Item AS i ON i.Id = v.ItemId
    JOIN core.VatRate AS vr ON vr.Id = i.DefaultVatRateId
    OUTER APPLY (SELECT TOP (1) x.Price FROM inv.RetailPrice AS x
                 WHERE x.WarehouseId = r.WarehouseId AND x.ItemVariantId = r.ItemVariantId AND x.ValidFrom <= @MovementDate
                 ORDER BY x.ValidFrom DESC) AS p;

    -- Predarea dintr-o gestiune la pret cu amanuntul descarca adaosul si TVA-ul pe loc (metoda 2), orice metoda ar avea gestiunea.
    UPDATE r SET RetailDischargeMethod = 2
    FROM @Rows AS r JOIN inv.MovementType AS t ON t.Id = r.MovementTypeId
    WHERE r.RetailDischargeMethod IS NOT NULL AND t.Code = 'TRANSFER_OUT';

    UPDATE r SET RetailPrice = m.RetailPrice, RetailAmount = -m.RetailAmount, RetailVatAmount = -m.RetailVatAmount,
        RetailDischargeMethod = ISNULL(m.RetailDischargeMethod, r.RetailDischargeMethod)
    FROM @Rows AS r
    JOIN inv.StockMovement AS m ON m.Id = r.ReversalOfId
    WHERE r.RetailDischargeMethod IS NOT NULL;
    UPDATE @Rows SET RetailPrice = NULL, RetailAmount = NULL, RetailVatAmount = NULL WHERE RetailPrice IS NULL OR RetailAmount IS NULL;
    UPDATE @Rows SET RetailDischargeMethod = NULL WHERE RetailAmount IS NULL AND ReversalOfId IS NOT NULL;

    IF EXISTS (SELECT 1 FROM @Rows WHERE RetailDischargeMethod IS NOT NULL AND RetailPrice IS NULL AND ReversalOfId IS NULL)
    BEGIN
        DECLARE @NoPrice nvarchar(400);
        SELECT TOP (1) @NoPrice = CONCAT(N'Varianta ', v.Code, N' nu are preț cu amănuntul în gestiunea ', w.Code, N' la ',
            CONVERT(nvarchar(10), @MovementDate, 104), N'; stabiliți prețul cu un document de modificare de preț (MP).')
        FROM @Rows AS r JOIN core.ItemVariant AS v ON v.Id = r.ItemVariantId JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId
        WHERE r.RetailDischargeMethod IS NOT NULL AND r.RetailPrice IS NULL AND r.ReversalOfId IS NULL;
        THROW 54311, @NoPrice, 1;
    END;

    -- 3. Randurile de sold: create daca lipsesc, apoi blocate in ordinea cheii.
    INSERT INTO inv.Stock (CompanyId, WarehouseId, DepotId, ItemVariantId, LotId)
    SELECT DISTINCT @CompanyId, r.WarehouseId, r.DepotId, r.ItemVariantId, r.LotId
    FROM @Rows AS r
    WHERE NOT EXISTS (SELECT 1 FROM inv.Stock AS s WITH (UPDLOCK, HOLDLOCK)
                      WHERE s.WarehouseId = r.WarehouseId AND s.DepotKey = ISNULL(r.DepotId, 0) AND s.ItemVariantId = r.ItemVariantId
                        AND s.LotKey = ISNULL(r.LotId, 0));

    -- Randurile de sold sunt blocate de inv.usp_StockCost_Load inaintea calculului; aici se reblocheaza in aceeasi ordine (fara efect nou).
    DECLARE @Locked TABLE (StockId bigint PRIMARY KEY);
    INSERT INTO @Locked
    SELECT s.Id FROM inv.Stock AS s WITH (UPDLOCK, ROWLOCK)
    WHERE EXISTS (SELECT 1 FROM @Rows AS r WHERE r.WarehouseId = s.WarehouseId AND ISNULL(r.DepotId, 0) = s.DepotKey
                  AND r.ItemVariantId = s.ItemVariantId AND ISNULL(r.LotId, 0) = s.LotKey)
    ORDER BY s.WarehouseId, s.DepotKey, s.ItemVariantId, s.LotKey;

    -- Soldurile pe zile.
    MERGE inv.StockDayBalance WITH (HOLDLOCK) AS target
    USING (SELECT WarehouseId, DepotId, ItemVariantId, LotId, SUM(CASE WHEN QtyBase > 0 THEN QtyBase ELSE 0 END) AS QtyIn,
               SUM(CASE WHEN QtyBase < 0 THEN -QtyBase ELSE 0 END) AS QtyOut
           FROM @Rows GROUP BY WarehouseId, DepotId, ItemVariantId, LotId) AS source
    ON target.WarehouseId = source.WarehouseId AND target.DepotKey = ISNULL(source.DepotId, 0) AND target.ItemVariantId = source.ItemVariantId
       AND target.LotKey = ISNULL(source.LotId, 0) AND target.BalanceDate = @MovementDate
    WHEN MATCHED THEN UPDATE SET QtyIn = target.QtyIn + source.QtyIn, QtyOut = target.QtyOut + source.QtyOut
    WHEN NOT MATCHED THEN INSERT (WarehouseId, DepotId, ItemVariantId, LotId, BalanceDate, QtyIn, QtyOut)
        VALUES (source.WarehouseId, source.DepotId, source.ItemVariantId, source.LotId, @MovementDate, source.QtyIn, source.QtyOut);

    -- 4. Stocul negativ la data miscarii si la toate datele ulterioare, pe cheile cu iesiri.
    DECLARE @Negative TABLE (WarehouseId int, DepotKey int, ItemVariantId int, LotKey int, BalanceDate date, Balance decimal(19,6));
    WITH Touched AS (
        SELECT DISTINCT WarehouseId, ISNULL(DepotId, 0) AS DepotKey, ItemVariantId, ISNULL(LotId, 0) AS LotKey FROM @Rows WHERE Direction = -1
    ),
    Running AS (
        SELECT b.WarehouseId, b.DepotKey, b.ItemVariantId, b.LotKey, b.BalanceDate,
            SUM(b.QtyIn - b.QtyOut) OVER (PARTITION BY b.WarehouseId, b.DepotKey, b.ItemVariantId, b.LotKey ORDER BY b.BalanceDate
                                          ROWS UNBOUNDED PRECEDING) AS Balance
        FROM inv.StockDayBalance AS b
        JOIN Touched AS t ON t.WarehouseId = b.WarehouseId AND t.DepotKey = b.DepotKey AND t.ItemVariantId = b.ItemVariantId AND t.LotKey = b.LotKey
    )
    INSERT INTO @Negative
    SELECT WarehouseId, DepotKey, ItemVariantId, LotKey, BalanceDate, Balance FROM Running WHERE BalanceDate >= @MovementDate AND Balance < 0;

    IF @IsExternalFact = 0 AND EXISTS (
        SELECT 1 FROM @Negative AS n
        JOIN @Rows AS r ON r.WarehouseId = n.WarehouseId AND ISNULL(r.DepotId, 0) = n.DepotKey AND r.ItemVariantId = n.ItemVariantId
            AND ISNULL(r.LotId, 0) = n.LotKey
        WHERE r.Direction = -1 AND r.NegativePolicy = 1)
    BEGIN
        DECLARE @Message nvarchar(400);
        SELECT TOP (1) @Message = CONCAT(N'Stoc insuficient: ', v.Code, N' în gestiunea ', w.Code, N' pe ', CONVERT(nvarchar(10), n.BalanceDate, 104),
            N' lipsesc ', FORMAT(-n.Balance, N'0.######', 'ro-RO'), N'.')
        FROM @Negative AS n
        JOIN @Rows AS r ON r.WarehouseId = n.WarehouseId AND ISNULL(r.DepotId, 0) = n.DepotKey AND r.ItemVariantId = n.ItemVariantId
            AND ISNULL(r.LotId, 0) = n.LotKey AND r.Direction = -1 AND r.NegativePolicy = 1
        JOIN core.ItemVariant AS v ON v.Id = n.ItemVariantId
        JOIN inv.Warehouse AS w ON w.Id = n.WarehouseId
        ORDER BY n.BalanceDate;
        THROW 54101, @Message, 1;
    END;

    UPDATE r SET IsForcedNegative = 1
    FROM @Rows AS r
    WHERE r.Direction = -1 AND EXISTS (SELECT 1 FROM @Negative AS n WHERE n.WarehouseId = r.WarehouseId AND n.DepotKey = ISNULL(r.DepotId, 0)
                                       AND n.ItemVariantId = r.ItemVariantId AND n.LotKey = ISNULL(r.LotId, 0));

    -- Miscarile: intai cele fara pereche, apoi intrarile transferurilor, cu CounterMovementId spre iesirea lor.
    DECLARE @Inserted TABLE (MovementId bigint, SourceLineId bigint, SourceRole tinyint, PostingSeq bigint, LotKey int, SerialKey int);
    INSERT INTO inv.StockMovement (CompanyId, DepotId, WarehouseId, ItemVariantId, LotId, SerialId, MovementDate, AccountingDate, PostingSeq,
        MovementTypeId, Direction, QtyBase, UnitCost, CostAmount, RetailPrice, RetailAmount, RetailVatAmount, RetailDischargeMethod, ReasonId, PartnerId, CostCenterId, ProjectId,
        EmployeeId, SourceModule, SourceDocumentType, SourceDocumentId, SourceDocumentNumber, SourceLineId, SourceRole, ReversalOfId, IsForcedNegative, LotOverrideReason,
        CreatedBy)
    OUTPUT inserted.Id, inserted.SourceLineId, inserted.SourceRole, inserted.PostingSeq, inserted.LotKey, inserted.SerialKey INTO @Inserted
    SELECT @CompanyId, r.DepotId, r.WarehouseId, r.ItemVariantId, r.LotId, r.SerialId, @MovementDate, @AccountingDate,
        NEXT VALUE FOR inv.seq_StockPosting OVER (ORDER BY r.RowNo), r.MovementTypeId, r.Direction, r.QtyBase, r.UnitCost, r.CostAmount, r.RetailPrice,
        r.RetailAmount, r.RetailVatAmount, CASE WHEN r.RetailAmount IS NULL THEN NULL ELSE r.RetailDischargeMethod END, r.ReasonId,
        @PartnerId, r.CostCenterId, r.ProjectId, @EmployeeId, @SourceModule, @SourceDocumentType, @SourceDocumentId, @SourceDocumentNumber,
        r.SourceLineId, r.SourceRole, r.ReversalOfId, r.IsForcedNegative, r.LotOverrideReason, @UserId
    FROM @Rows AS r
    WHERE r.CounterRowNo IS NULL;
    UPDATE r SET MovementId = i.MovementId, PostingSeq = i.PostingSeq
    FROM @Rows AS r
    JOIN @Inserted AS i ON i.SourceLineId = r.SourceLineId AND i.SourceRole = r.SourceRole AND i.LotKey = ISNULL(r.LotId, 0) AND i.SerialKey = ISNULL(r.SerialId, 0);

    DELETE FROM @Inserted;
    INSERT INTO inv.StockMovement (CompanyId, DepotId, WarehouseId, ItemVariantId, LotId, SerialId, MovementDate, AccountingDate, PostingSeq,
        MovementTypeId, Direction, QtyBase, UnitCost, CostAmount, RetailPrice, RetailAmount, RetailVatAmount, RetailDischargeMethod, ReasonId, PartnerId, CostCenterId, ProjectId,
        EmployeeId, SourceModule, SourceDocumentType, SourceDocumentId, SourceDocumentNumber, SourceLineId, SourceRole, CounterMovementId, ReversalOfId,
        IsForcedNegative, CreatedBy)
    OUTPUT inserted.Id, inserted.SourceLineId, inserted.SourceRole, inserted.PostingSeq, inserted.LotKey, inserted.SerialKey INTO @Inserted
    SELECT @CompanyId, r.DepotId, r.WarehouseId, r.ItemVariantId, r.LotId, r.SerialId, @MovementDate, @AccountingDate,
        NEXT VALUE FOR inv.seq_StockPosting OVER (ORDER BY r.RowNo), r.MovementTypeId, r.Direction, r.QtyBase, r.UnitCost, r.CostAmount, r.RetailPrice,
        r.RetailAmount, r.RetailVatAmount, CASE WHEN r.RetailAmount IS NULL THEN NULL ELSE r.RetailDischargeMethod END, r.ReasonId,
        @PartnerId, r.CostCenterId, r.ProjectId, @EmployeeId, @SourceModule, @SourceDocumentType, @SourceDocumentId, @SourceDocumentNumber,
        r.SourceLineId, r.SourceRole, c.MovementId, r.ReversalOfId, r.IsForcedNegative, @UserId
    FROM @Rows AS r
    JOIN @Rows AS c ON c.RowNo = r.CounterRowNo
    WHERE r.CounterRowNo IS NOT NULL;
    UPDATE r SET MovementId = i.MovementId, PostingSeq = i.PostingSeq
    FROM @Rows AS r
    JOIN @Inserted AS i ON i.SourceLineId = r.SourceLineId AND i.SourceRole = r.SourceRole AND i.LotKey = ISNULL(r.LotId, 0) AND i.SerialKey = ISNULL(r.SerialId, 0)
    WHERE r.MovementId IS NULL;

    -- Soldul curent si prima miscare a gestiunii (blocheaza codul, tipul si baza de evaluare, S1 3.1).
    -- Straturile noi (cu Id-ul din calcul mapat), straturile existente atinse, consumurile si regularizarile (S3 3.2, 3.4).
    DECLARE @LayerMap TABLE (LayerRef int PRIMARY KEY, LayerId bigint);
    MERGE inv.CostLayer AS target
    USING (SELECT l.*, COALESCE(l.MovementId, r.MovementId) AS OwnerMovementId, COALESCE(l.SeqOverride, r.PostingSeq) AS Seq
           FROM @Layers AS l LEFT JOIN @Rows AS r ON r.RowNo = l.RowNo WHERE l.LayerRef IS NOT NULL) AS source
    ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (CompanyId, WarehouseId, DepotId, ItemVariantId, LotId, MovementId, OriginLayerId, LayerDate, PostingSeq, QtyIn, QtyRemaining, ValueRemaining,
            UnitCost, IsNegative, CreatedBy)
        VALUES (@CompanyId, source.WarehouseId, source.DepotId, source.ItemVariantId, source.LotId, source.OwnerMovementId, source.OriginLayerId,
            source.LayerDate, source.Seq, source.QtyIn, source.QtyRemaining, source.ValueRemaining, source.UnitCost, source.IsNegative, @UserId)
    OUTPUT source.LayerRef, inserted.Id INTO @LayerMap;

    UPDATE c SET QtyRemaining = l.QtyRemaining, ValueRemaining = l.ValueRemaining, ModifiedAt = SYSUTCDATETIME()
    FROM inv.CostLayer AS c JOIN @Layers AS l ON l.LayerId = c.Id
    WHERE l.LayerRef IS NULL;

    INSERT INTO inv.CostLayerConsumption (OutboundMovementId, CostLayerId, Qty, CostAmount)
    SELECT COALESCE(x.MovementId, r.MovementId), COALESCE(x.LayerId, m.LayerId), x.Qty, x.CostAmount
    FROM @Consumptions AS x
    LEFT JOIN @Rows AS r ON r.RowNo = x.RowNo
    LEFT JOIN @LayerMap AS m ON m.LayerRef = x.LayerRef;

    INSERT INTO inv.CostAdjustment (CompanyId, WarehouseId, DepotId, ItemVariantId, LotId, MovementId, Kind, InStock, Amount, AccountingDate,
        SourceDocumentType, SourceDocumentId, CreatedBy)
    SELECT @CompanyId, a.WarehouseId, a.DepotId, a.ItemVariantId, a.LotId, COALESCE(a.MovementId, r.MovementId), a.Kind, a.InStock, a.Amount,
        @AccountingDate, @SourceDocumentType, @SourceDocumentId, @UserId
    FROM @Adjustments AS a
    LEFT JOIN @Rows AS r ON r.RowNo = a.RowNo;

    UPDATE s
    SET QtyOnHand = k.Qty, CostOnHand = k.Value, LastUnitCost = k.LastUnitCost, LastMovementAt = SYSUTCDATETIME()
    FROM inv.Stock AS s
    JOIN @KeyStates AS k ON k.WarehouseId = s.WarehouseId AND ISNULL(k.DepotId, 0) = s.DepotKey AND k.ItemVariantId = s.ItemVariantId
        AND ISNULL(k.LotId, 0) = s.LotKey;

    UPDATE s SET RetailValueOnHand = s.RetailValueOnHand + x.Amount
    FROM inv.Stock AS s
    JOIN (SELECT WarehouseId, ISNULL(DepotId, 0) AS DepotKey, ItemVariantId, ISNULL(LotId, 0) AS LotKey, SUM(RetailAmount) AS Amount
          FROM @Rows WHERE RetailAmount IS NOT NULL GROUP BY WarehouseId, ISNULL(DepotId, 0), ItemVariantId, ISNULL(LotId, 0)) AS x
        ON x.WarehouseId = s.WarehouseId AND x.DepotKey = s.DepotKey AND x.ItemVariantId = s.ItemVariantId AND x.LotKey = s.LotKey;

    -- Starea seriilor dupa ultima lor miscare din apel (S4 3.4): intrarea le pune in stoc (returul clientului cu starea 4, tranzitul cu 2),
    -- vanzarea si returul la furnizor le scot ca vandute (cu garantia la vanzare), descarcarea si stornarea intrarii le caseaza.
    WITH LastMove AS (
        SELECT r.*, ROW_NUMBER() OVER (PARTITION BY r.SerialId ORDER BY r.RowNo DESC) AS N FROM @Rows AS r WHERE r.SerialId IS NOT NULL
    )
    UPDATE s
    SET Status = CASE WHEN l.Direction = 1 THEN CASE WHEN t.Code = 'RETURN_FROM_CUSTOMER' AND l.ReversalOfId IS NULL THEN 4 WHEN w.Kind = 3 THEN 2 ELSE 1 END
                      WHEN l.ReversalOfId IS NOT NULL THEN 5
                      WHEN t.Code IN ('ISSUE_SALE', 'POS_SALE', 'RETURN_TO_SUPPLIER') THEN 3
                      WHEN t.Code = 'TRANSFER_OUT' THEN 2
                      ELSE 5 END,
        WarehouseId = CASE WHEN l.Direction = 1 OR t.Code = 'TRANSFER_OUT' THEN l.WarehouseId END,
        DepotId = CASE WHEN l.Direction = 1 THEN l.DepotId END,
        LotId = COALESCE(l.LotId, s.LotId),
        SoldAt = CASE WHEN l.Direction = -1 AND l.ReversalOfId IS NULL AND t.Code IN ('ISSUE_SALE', 'POS_SALE') THEN @MovementDate
                      WHEN l.Direction = 1 THEN NULL ELSE s.SoldAt END,
        SoldToPartnerId = CASE WHEN l.Direction = -1 AND l.ReversalOfId IS NULL AND t.Code IN ('ISSUE_SALE', 'POS_SALE', 'RETURN_TO_SUPPLIER') THEN @PartnerId
                               WHEN l.Direction = 1 THEN NULL ELSE s.SoldToPartnerId END,
        WarrantyUntil = CASE WHEN l.Direction = -1 AND l.ReversalOfId IS NULL AND t.Code IN ('ISSUE_SALE', 'POS_SALE') AND iss.WarrantyMonths IS NOT NULL
                             THEN DATEADD(MONTH, iss.WarrantyMonths, @MovementDate) WHEN l.Direction = 1 THEN NULL ELSE s.WarrantyUntil END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM inv.Serial AS s
    JOIN LastMove AS l ON l.SerialId = s.Id AND l.N = 1
    JOIN inv.MovementType AS t ON t.Id = l.MovementTypeId
    JOIN inv.Warehouse AS w ON w.Id = l.WarehouseId
    JOIN core.ItemVariant AS v ON v.Id = s.ItemVariantId
    LEFT JOIN inv.ItemStockSettings AS iss ON iss.CompanyId = @CompanyId AND iss.ItemId = v.ItemId;

    -- Prima intrare a fiecarui lot nou (trasabilitatea inapoi, S4 3.2).
    UPDATE x SET FirstReceiptMovementId = f.MovementId
    FROM inv.Lot AS x
    CROSS APPLY (SELECT TOP (1) r.MovementId FROM @Rows AS r WHERE r.LotId = x.Id AND r.Direction = 1 ORDER BY r.RowNo) AS f
    WHERE x.FirstReceiptMovementId IS NULL AND x.IsTechnical = 0;

    UPDATE w SET FirstMovementAt = SYSUTCDATETIME()
    FROM inv.Warehouse AS w
    WHERE w.FirstMovementAt IS NULL AND EXISTS (SELECT 1 FROM @Rows AS r WHERE r.WarehouseId = w.Id);

    SELECT r.MovementId, r.SourceLineId, r.SourceRole, r.WarehouseId, r.DepotId, r.ItemVariantId, r.QtyBase, r.UnitCost, r.CostAmount, r.IsForcedNegative,
        t.Code AS MovementTypeCode, mr.Code AS ReasonCode, CAST(0 AS bit) AS Replayed, r.RetailAmount, r.RetailVatAmount,
        CASE WHEN r.RetailAmount IS NULL THEN NULL ELSE r.RetailDischargeMethod END AS RetailDischargeMethod,
        COALESCE(r.CostCenterId, w.DefaultCostCenterId) AS CostCenterId, r.LotId, lot.LotCode, lot.CkuCode, lot.CkuUid, lot.ExpiryDate, r.SerialId,
        ser.SerialNo, ser.WarrantyUntil
    FROM @Rows AS r
    JOIN inv.MovementType AS t ON t.Id = r.MovementTypeId
    JOIN inv.Warehouse AS w ON w.Id = r.WarehouseId
    LEFT JOIN inv.Lot AS lot ON lot.Id = r.LotId
    LEFT JOIN inv.Serial AS ser ON ser.Id = r.SerialId
    LEFT JOIN inv.MovementReason AS mr ON mr.Id = r.ReasonId
    ORDER BY r.RowNo;
END;
