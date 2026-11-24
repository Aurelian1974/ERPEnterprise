CREATE OR ALTER PROCEDURE fin.usp_Allocation_CreateBatch
    @CompanyId   int,
    @Allocations fin.AllocationList READONLY,
    @Corrections fin.FxCorrectionList READONLY,
    @UserId      int
AS
BEGIN
    -- Un lot de alocari (02.02, F3 3.3, 3.7, 7), in tranzactia apelantului. Partidele atinse se blocheaza in ordinea Id-urilor (SP-16), apoi:
    -- debit cu credit (51401), acelasi partener, rol si firma in afara compensarii (51402), monede diferite doar cu curs incrucisat (51418),
    -- totalul pe partida nu depaseste restul, pe fiecare parte (51403). Sumele pe parti, valorile contabile in lei, cursul incrucisat si
    -- diferenta de curs vin calculate de FxCalculator (F4 4.1) pe partidele blocate inainte; procedura scrie alocarile, scade restul si
    -- valoarea lui in lei pe ambele partide si scrie fin.FxDifference (F4 3.2). TVA-ul
    -- exigibil (3.7) se calculeaza aici, sub blocare, pe partidele cu TVA la incasare (facturi, facturi de avans, note de debit), in afara
    -- alocarilor de storno: proportional, iar la alocarea care inchide factura, restul, ca suma sa dea exact TVA-ul facturii (P7).
    -- Corectiile F4 4.6 (alocare cu data intr-o luna deja reevaluata) se scriu in asteptare, pentru rularea urmatoare.
    -- Intoarce: 1) alocarile; 2) partidele inchise; 3) randurile de TVA la incasare; 4) diferentele de curs.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Items TABLE (Id bigint PRIMARY KEY, CompanyId int, PartnerId int, Role tinyint, Kind tinyint, CurrencyCode char(3), ExchangeRate decimal(19,6),
                          Amount decimal(19,2), AmountRon decimal(19,2), OpenAmount decimal(19,2), DocumentDate date, VatAmountRon decimal(19,2),
                          IsVatOnCollection bit);
    INSERT INTO @Items
    SELECT o.Id, o.CompanyId, o.PartnerId, o.Role, o.Kind, o.CurrencyCode, o.ExchangeRate, o.Amount, o.AmountRon, o.OpenAmount, o.DocumentDate,
        o.VatAmountRon, o.IsVatOnCollection
    FROM fin.OpenItem AS o WITH (UPDLOCK, ROWLOCK)
    WHERE o.Id IN (SELECT DebitOpenItemId FROM @Allocations UNION SELECT CreditOpenItemId FROM @Allocations)
    ORDER BY o.Id;

    IF EXISTS (SELECT 1 FROM @Allocations AS a WHERE a.DebitAmount <= 0 OR a.CreditAmount <= 0)
        THROW 51403, N'Suma alocată trebuie să fie pozitivă.', 1;
    IF EXISTS (SELECT 1 FROM @Allocations AS a
               LEFT JOIN @Items AS d ON d.Id = a.DebitOpenItemId
               LEFT JOIN @Items AS c ON c.Id = a.CreditOpenItemId
               WHERE d.Id IS NULL OR c.Id IS NULL OR d.CompanyId <> @CompanyId OR c.CompanyId <> @CompanyId)
        THROW 51021, N'Partida nu există în firma curentă.', 1;
    IF EXISTS (SELECT 1 FROM @Allocations AS a
               JOIN @Items AS d ON d.Id = a.DebitOpenItemId
               JOIN @Items AS c ON c.Id = a.CreditOpenItemId
               WHERE d.Amount < 0 OR c.Amount > 0)
        THROW 51401, N'Alocarea trebuie să lege o partidă de debit cu una de credit.', 1;
    IF EXISTS (SELECT 1 FROM @Allocations AS a
               JOIN @Items AS d ON d.Id = a.DebitOpenItemId
               JOIN @Items AS c ON c.Id = a.CreditOpenItemId
               WHERE a.Source <> 3 AND (d.PartnerId <> c.PartnerId OR d.Role <> c.Role))
        THROW 51402, N'Partidele aparțin altor parteneri sau roluri.', 1;
    IF EXISTS (SELECT 1 FROM @Allocations AS a
               JOIN @Items AS d ON d.Id = a.DebitOpenItemId
               JOIN @Items AS c ON c.Id = a.CreditOpenItemId
               WHERE (d.CurrencyCode <> c.CurrencyCode AND a.CrossRate IS NULL) OR (d.CurrencyCode = c.CurrencyCode AND a.DebitAmount <> a.CreditAmount))
        THROW 51418, N'Partidele au monede diferite; alocarea cere un curs încrucișat.', 1;
    IF EXISTS (SELECT 1 FROM @Items AS i
               CROSS APPLY (SELECT SUM(a.DebitAmount) AS D FROM @Allocations AS a WHERE a.DebitOpenItemId = i.Id) AS dd
               CROSS APPLY (SELECT SUM(a.CreditAmount) AS C FROM @Allocations AS a WHERE a.CreditOpenItemId = i.Id) AS cc
               WHERE (i.Amount > 0 AND ISNULL(dd.D, 0) > i.OpenAmount) OR (i.Amount < 0 AND ISNULL(cc.C, 0) > -i.OpenAmount))
        THROW 51403, N'Suma alocată depășește restul de plată.', 1;

    DECLARE @Rows TABLE (RowNo int PRIMARY KEY, DebitOpenItemId bigint, CreditOpenItemId bigint, DebitAmount decimal(19,2), CreditAmount decimal(19,2),
                         AllocationDate date, Source tinyint, AmountRonDebit decimal(19,2), AmountRonCredit decimal(19,2), CompensationId int, WriteOffId int,
                         CommercialPaperId int, CrossRate decimal(19,6), CrossRateSource tinyint, DifferenceRon decimal(19,2), FxOpenItemId bigint);
    INSERT INTO @Rows
    SELECT a.RowNo, a.DebitOpenItemId, a.CreditOpenItemId, a.DebitAmount, a.CreditAmount, a.AllocationDate, a.Source, a.AmountRonDebit, a.AmountRonCredit,
        a.CompensationId, a.WriteOffId, a.CommercialPaperId, a.CrossRate, a.CrossRateSource, a.DifferenceRon, a.FxOpenItemId
    FROM @Allocations AS a;

    DECLARE @Inserted TABLE (AllocationId bigint, RowNo int);
    MERGE fin.Allocation AS t
    USING @Rows AS a ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (CompanyId, DebitOpenItemId, CreditOpenItemId, DebitAmount, CreditAmount, AmountRonDebit, AmountRonCredit, AllocationDate, Source,
                CompensationId, WriteOffId, CommercialPaperId, CrossRate, CrossRateSource, Status, CreatedBy)
        VALUES (@CompanyId, a.DebitOpenItemId, a.CreditOpenItemId, a.DebitAmount, a.CreditAmount, a.AmountRonDebit, a.AmountRonCredit, a.AllocationDate,
                a.Source, a.CompensationId, a.WriteOffId, a.CommercialPaperId, a.CrossRate, a.CrossRateSource, 1, @UserId)
    OUTPUT inserted.Id, a.RowNo INTO @Inserted (AllocationId, RowNo);

    -- Restul de plata, valoarea lui contabila in lei (F4 4.1 pasul 6) si data inchiderii.
    UPDATE o
    SET OpenAmount    = o.OpenAmount - x.Delta,
        OpenAmountRon = CASE WHEN o.OpenAmount - x.Delta = 0 THEN 0 ELSE o.OpenAmountRon - x.DeltaRon END,
        ClosedAt      = CASE WHEN o.OpenAmount - x.Delta = 0 THEN x.LastDate END,
        ModifiedAt    = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.OpenItem AS o
    JOIN (SELECT ItemId, SUM(Delta) AS Delta, SUM(DeltaRon) AS DeltaRon, MAX(AllocationDate) AS LastDate
          FROM (SELECT DebitOpenItemId AS ItemId, DebitAmount AS Delta, AmountRonDebit AS DeltaRon, AllocationDate FROM @Rows
                UNION ALL
                SELECT CreditOpenItemId, -CreditAmount, -AmountRonCredit, AllocationDate FROM @Rows) AS u
          GROUP BY ItemId) AS x ON x.ItemId = o.Id;

    INSERT INTO fin.FxRevaluationCorrection (CompanyId, OpenItemId, RunId, AllocationId, ForeignAmount, DifferenceRon, CreatedBy)
    SELECT @CompanyId, c.OpenItemId, c.RunId, ins.AllocationId, c.ForeignAmount, c.DifferenceRon, @UserId
    FROM @Corrections AS c JOIN @Inserted AS ins ON ins.RowNo = c.RowNo;

    -- Diferentele de curs realizate (F4 3.2): partida in valuta, suma ei, valoarea ei contabila si valoarea de decontare (cealalta parte).
    DECLARE @FxOut TABLE (Id bigint, AllocationId bigint);
    INSERT INTO fin.FxDifference (CompanyId, DifferenceDate, Source, AllocationId, PartnerId, Role, OpenItemId, CurrencyCode, ForeignAmount, CarryingAmountRon,
        SettlementAmountRon, DifferenceRon, GlAccountCode, CreatedBy)
    OUTPUT inserted.Id, inserted.AllocationId INTO @FxOut (Id, AllocationId)
    SELECT @CompanyId, r.AllocationDate, 1, ins.AllocationId, f.PartnerId, f.Role, r.FxOpenItemId, f.CurrencyCode,
        CASE WHEN r.FxOpenItemId = r.DebitOpenItemId THEN r.DebitAmount ELSE r.CreditAmount END,
        CASE WHEN r.FxOpenItemId = r.DebitOpenItemId THEN r.AmountRonDebit ELSE r.AmountRonCredit END,
        CASE WHEN r.FxOpenItemId = r.DebitOpenItemId THEN r.AmountRonCredit ELSE r.AmountRonDebit END,
        r.DifferenceRon, CASE WHEN r.DifferenceRon > 0 THEN N'765' ELSE N'665' END, @UserId
    FROM @Rows AS r
    JOIN @Inserted AS ins ON ins.RowNo = r.RowNo
    JOIN @Items AS f ON f.Id = r.FxOpenItemId
    WHERE r.DifferenceRon <> 0;

    -- Ratele: restul de plata se repartizeaza pe scadente, in ordinea lor.
    DECLARE @Touched nvarchar(max) = (SELECT Id FROM @Items FOR JSON PATH);
    EXEC fin.usp_OpenItemSchedule_Refresh @Touched;

    -- TVA la incasare: partida cu TVA din fiecare alocare (debitul sau creditul), in ordinea randurilor din lot.
    DECLARE @Vat TABLE (RowNo int, ItemId bigint, Amount decimal(19,2), PRIMARY KEY (RowNo, ItemId));
    INSERT INTO @Vat (RowNo, ItemId, Amount)
    SELECT r.RowNo, v.ItemId, v.Amount
    FROM @Rows AS r
    CROSS APPLY (VALUES (r.DebitOpenItemId, r.DebitAmount), (r.CreditOpenItemId, r.CreditAmount)) AS v(ItemId, Amount)
    JOIN @Items AS i ON i.Id = v.ItemId
    WHERE r.Source <> 4 AND i.IsVatOnCollection = 1 AND i.Kind IN (1, 3, 4, 6) AND i.VatAmountRon <> 0;

    DECLARE @VatOut TABLE (Id bigint, OpenItemId bigint, AllocationId bigint, Side tinyint, ExigibilityDate date, BaseAmountRon decimal(19,2),
                           VatAmountRon decimal(19,2));
    WITH Calc AS (
        SELECT v.RowNo, v.ItemId, i.CompanyId, i.Role, r.AllocationDate, ins.AllocationId,
            ABS(i.OpenAmount) - SUM(v.Amount) OVER (PARTITION BY v.ItemId ORDER BY v.RowNo ROWS UNBOUNDED PRECEDING) AS OpenAfter,
            ROUND(v.Amount * ABS(i.VatAmountRon) / ABS(i.Amount), 2) AS PropVat,
            ROUND(v.Amount * (ABS(i.AmountRon) - ABS(i.VatAmountRon)) / ABS(i.Amount), 2) AS PropBase,
            ABS(i.VatAmountRon) AS TotalVat, ABS(i.AmountRon) - ABS(i.VatAmountRon) AS TotalBase
        FROM @Vat AS v
        JOIN @Items AS i ON i.Id = v.ItemId
        JOIN @Rows AS r ON r.RowNo = v.RowNo
        JOIN @Inserted AS ins ON ins.RowNo = v.RowNo),
    WithPrevious AS (
        SELECT c.*,
            ISNULL(SUM(c.PropVat) OVER (PARTITION BY c.ItemId ORDER BY c.RowNo ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS BatchVat,
            ISNULL(SUM(c.PropBase) OVER (PARTITION BY c.ItemId ORDER BY c.RowNo ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS BatchBase
        FROM Calc AS c)
    INSERT INTO fin.VatCashEntry (CompanyId, OpenItemId, AllocationId, Side, ExigibilityDate, BaseAmountRon, VatAmountRon, IsReversal, CreatedBy)
    OUTPUT inserted.Id, inserted.OpenItemId, inserted.AllocationId, inserted.Side, inserted.ExigibilityDate, inserted.BaseAmountRon, inserted.VatAmountRon
        INTO @VatOut
    SELECT w.CompanyId, w.ItemId, w.AllocationId, w.Role, w.AllocationDate,
        CASE WHEN w.OpenAfter = 0 THEN w.TotalBase - prior.Base - w.BatchBase ELSE w.PropBase END,
        CASE WHEN w.OpenAfter = 0 THEN w.TotalVat - prior.Vat - w.BatchVat ELSE w.PropVat END,
        0, @UserId
    FROM WithPrevious AS w
    CROSS APPLY (SELECT ISNULL(SUM(e.VatAmountRon), 0) AS Vat, ISNULL(SUM(e.BaseAmountRon), 0) AS Base
                 FROM fin.VatCashEntry AS e WHERE e.OpenItemId = w.ItemId) AS prior;

    SELECT ins.AllocationId, r.RowNo, d.PartnerId, d.Role, r.DebitOpenItemId, d.SourceDocumentType AS DebitSourceDocumentType,
        d.SourceDocumentId AS DebitSourceDocumentId, d.CurrencyCode AS DebitCurrencyCode, d.ExchangeRate AS DebitExchangeRate, r.CreditOpenItemId,
        c.SourceDocumentType AS CreditSourceDocumentType, c.SourceDocumentId AS CreditSourceDocumentId, c.CurrencyCode AS CreditCurrencyCode,
        c.ExchangeRate AS CreditExchangeRate, r.DebitAmount AS Amount, r.AmountRonDebit, r.AmountRonCredit, r.AllocationDate, r.Source, r.CreditAmount
    FROM @Inserted AS ins
    JOIN @Rows AS r ON r.RowNo = ins.RowNo
    JOIN fin.OpenItem AS d ON d.Id = r.DebitOpenItemId
    JOIN fin.OpenItem AS c ON c.Id = r.CreditOpenItemId
    ORDER BY r.RowNo;

    SELECT o.Id, o.PartnerId, o.Role, o.Kind, o.SourceDocumentType, o.SourceDocumentId, o.ClosedAt
    FROM fin.OpenItem AS o JOIN @Items AS i ON i.Id = o.Id
    WHERE o.OpenAmount = 0 AND i.OpenAmount <> 0;

    SELECT v.Id, v.OpenItemId, o.SourceDocumentType, o.SourceDocumentId, v.Side, v.ExigibilityDate, v.BaseAmountRon, v.VatAmountRon, CAST(0 AS bit) AS IsReversal
    FROM @VatOut AS v JOIN fin.OpenItem AS o ON o.Id = v.OpenItemId;

    SELECT x.Id, x.DifferenceDate, x.Source, x.AllocationId, x.PartnerId, x.Role, x.OpenItemId, x.CurrencyCode, x.ForeignAmount, x.DifferenceRon, x.IsIndexed,
        x.GlAccountCode, x.IsReversal
    FROM fin.FxDifference AS x JOIN @FxOut AS f ON f.Id = x.Id;
END;
