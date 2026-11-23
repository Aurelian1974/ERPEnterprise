CREATE OR ALTER PROCEDURE fin.usp_Allocation_CreateBatch
    @CompanyId   int,
    @Allocations fin.AllocationList READONLY,
    @UserId      int
AS
BEGIN
    -- Un lot de alocari (02.02, F3 3.3, 3.7, 7), in tranzactia apelantului. Partidele atinse se blocheaza in ordinea Id-urilor (SP-16), apoi:
    -- debit cu credit (51401), acelasi partener, rol si firma in afara compensarii (51402), aceeasi moneda (51418; monedele diferite vin cu
    -- F4), totalul pe partida nu depaseste restul (51403). Data alocarii, daca lipseste, e data documentului mai recent (regula 4). TVA-ul
    -- exigibil (3.7) se calculeaza aici, sub blocare, pe partidele cu TVA la incasare (facturi, facturi de avans, note de debit), in afara
    -- alocarilor de storno: proportional, iar la alocarea care inchide factura, restul, ca suma sa dea exact TVA-ul facturii (P7).
    -- Intoarce: 1) alocarile; 2) partidele inchise; 3) randurile de TVA la incasare.
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

    IF EXISTS (SELECT 1 FROM @Allocations AS a WHERE a.Amount <= 0)
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
               WHERE d.CurrencyCode <> c.CurrencyCode)
        THROW 51418, N'Partidele au monede diferite; alocarea între monede vine cu diferențele de curs (F4).', 1;
    IF EXISTS (SELECT 1 FROM @Items AS i
               CROSS APPLY (SELECT SUM(a.Amount) AS D FROM @Allocations AS a WHERE a.DebitOpenItemId = i.Id) AS dd
               CROSS APPLY (SELECT SUM(a.Amount) AS C FROM @Allocations AS a WHERE a.CreditOpenItemId = i.Id) AS cc
               WHERE (i.Amount > 0 AND ISNULL(dd.D, 0) > i.OpenAmount) OR (i.Amount < 0 AND ISNULL(cc.C, 0) > -i.OpenAmount))
        THROW 51403, N'Suma alocată depășește restul de plată.', 1;

    DECLARE @Rows TABLE (RowNo int PRIMARY KEY, DebitOpenItemId bigint, CreditOpenItemId bigint, Amount decimal(19,2), AllocationDate date, Source tinyint,
                         AmountRonDebit decimal(19,2), AmountRonCredit decimal(19,2), CompensationId int, WriteOffId int, CommercialPaperId int);
    INSERT INTO @Rows
    SELECT a.RowNo, a.DebitOpenItemId, a.CreditOpenItemId, a.Amount,
        ISNULL(a.AllocationDate, CASE WHEN d.DocumentDate >= c.DocumentDate THEN d.DocumentDate ELSE c.DocumentDate END), a.Source,
        ROUND(a.Amount * d.ExchangeRate, 2), ROUND(a.Amount * c.ExchangeRate, 2), a.CompensationId, a.WriteOffId, a.CommercialPaperId
    FROM @Allocations AS a
    JOIN @Items AS d ON d.Id = a.DebitOpenItemId
    JOIN @Items AS c ON c.Id = a.CreditOpenItemId;

    DECLARE @Inserted TABLE (AllocationId bigint, RowNo int);
    MERGE fin.Allocation AS t
    USING @Rows AS a ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (CompanyId, DebitOpenItemId, CreditOpenItemId, DebitAmount, CreditAmount, AmountRonDebit, AmountRonCredit, AllocationDate, Source,
                CompensationId, WriteOffId, CommercialPaperId, Status, CreatedBy)
        VALUES (@CompanyId, a.DebitOpenItemId, a.CreditOpenItemId, a.Amount, a.Amount, a.AmountRonDebit, a.AmountRonCredit, a.AllocationDate, a.Source,
                a.CompensationId, a.WriteOffId, a.CommercialPaperId, 1, @UserId)
    OUTPUT inserted.Id, a.RowNo INTO @Inserted (AllocationId, RowNo);

    -- Restul de plata si data inchiderii.
    UPDATE o
    SET OpenAmount = o.OpenAmount - x.Delta,
        ClosedAt   = CASE WHEN o.OpenAmount - x.Delta = 0 THEN x.LastDate END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    FROM fin.OpenItem AS o
    JOIN (SELECT ItemId, SUM(Delta) AS Delta, MAX(AllocationDate) AS LastDate
          FROM (SELECT DebitOpenItemId AS ItemId, Amount AS Delta, AllocationDate FROM @Rows
                UNION ALL
                SELECT CreditOpenItemId, -Amount, AllocationDate FROM @Rows) AS u
          GROUP BY ItemId) AS x ON x.ItemId = o.Id;

    -- Ratele: restul de plata se repartizeaza pe scadente, in ordinea lor.
    DECLARE @Touched nvarchar(max) = (SELECT Id FROM @Items FOR JSON PATH);
    EXEC fin.usp_OpenItemSchedule_Refresh @Touched;

    -- TVA la incasare: partida cu TVA din fiecare alocare (debitul sau creditul), in ordinea randurilor din lot.
    DECLARE @Vat TABLE (RowNo int, ItemId bigint, Amount decimal(19,2), PRIMARY KEY (RowNo, ItemId));
    INSERT INTO @Vat (RowNo, ItemId, Amount)
    SELECT r.RowNo, v.ItemId, r.Amount
    FROM @Rows AS r
    CROSS APPLY (VALUES (r.DebitOpenItemId), (r.CreditOpenItemId)) AS v(ItemId)
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
        c.ExchangeRate AS CreditExchangeRate, r.Amount, r.AmountRonDebit, r.AmountRonCredit, r.AllocationDate, r.Source
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
END;
