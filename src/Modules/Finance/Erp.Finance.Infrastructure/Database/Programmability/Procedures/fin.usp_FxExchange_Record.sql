CREATE OR ALTER PROCEDURE fin.usp_FxExchange_Record
    @CompanyId  int,
    @LineIds    nvarchar(max),
    @UserId     int
AS
BEGIN
    -- Diferenta realizata la schimbul valutar intre conturi proprii (F4 3.5, 4.3), dupa validarea liniilor de extras: pentru fiecare parte
    -- BANK-SCHIMB-VALUTAR a liniilor date, legata de perechea ei din celalalt cont, cand ambele linii sunt validate si perechea nu are inca
    -- diferenta. Valoarea in lei a fiecarei parti: suma, in lei; altfel suma la cursul BNR al zilei liniei. Diferenta = valoarea intrata −
    -- valoarea iesita (pozitiv = castig, 765; negativ = pierdere, 665). Intoarce diferentele scrise.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Pairs TABLE (PartId bigint PRIMARY KEY, PairPartId bigint);
    INSERT INTO @Pairs (PartId, PairPartId)
    SELECT p.Id, q.Id
    FROM fin.BankStatementLinePart AS p
    JOIN fin.TreasuryOperationType AS t ON t.Id = p.TreasuryOperationTypeId AND t.Code = N'BANK-SCHIMB-VALUTAR'
    JOIN fin.BankStatementLine AS l ON l.Id = p.BankStatementLineId AND l.MatchStatus = 4
    CROSS APPLY (SELECT TOP (1) q.Id FROM fin.BankStatementLinePart AS q
                 JOIN fin.BankStatementLine AS ql ON ql.Id = q.BankStatementLineId AND ql.MatchStatus = 4
                 WHERE q.TreasuryOperationTypeId = p.TreasuryOperationTypeId
                   AND (q.BankStatementLineId = p.PairedBankStatementLineId OR q.PairedBankStatementLineId = p.BankStatementLineId)
                 ORDER BY q.Id) AS q
    JOIN fin.TreasuryAccount AS a ON a.Id = l.TreasuryAccountId AND a.CompanyId = @CompanyId
    WHERE p.BankStatementLineId IN (SELECT CAST(j.value AS bigint) FROM OPENJSON(@LineIds) AS j)
      AND NOT EXISTS (SELECT 1 FROM fin.FxDifference AS x
                      WHERE x.Source = 2 AND x.BankStatementLinePartId IN (p.Id, q.Id) AND x.IsReversal = 0
                        AND NOT EXISTS (SELECT 1 FROM fin.FxDifference AS r WHERE r.ReversalOfId = x.Id));

    DECLARE @Values TABLE (PartId bigint PRIMARY KEY, CurrencyCode char(3), Amount decimal(19,2), Ron decimal(19,2), BookingDate date);
    INSERT INTO @Values (PartId, CurrencyCode, Amount, Ron, BookingDate)
    SELECT p.Id, a.CurrencyCode, p.Amount, CASE WHEN a.CurrencyCode = 'RON' THEN p.Amount ELSE ROUND(p.Amount * r.PerUnit, 2) END, l.BookingDate
    FROM fin.BankStatementLinePart AS p
    JOIN fin.BankStatementLine AS l ON l.Id = p.BankStatementLineId
    JOIN fin.TreasuryAccount AS a ON a.Id = l.TreasuryAccountId
    OUTER APPLY (SELECT TOP (1) x.Rate / x.Multiplier AS PerUnit FROM core.ExchangeRate AS x
                 WHERE x.CurrencyCode = a.CurrencyCode AND x.RateDate <= l.BookingDate ORDER BY x.RateDate DESC) AS r
    WHERE p.Id IN (SELECT PartId FROM @Pairs UNION SELECT PairPartId FROM @Pairs);

    IF EXISTS (SELECT 1 FROM @Values WHERE Ron IS NULL)
        THROW 51605, N'Lipsește cursul BNR pentru ziua schimbului valutar.', 1;

    DECLARE @FxOut TABLE (Id bigint);
    INSERT INTO fin.FxDifference (CompanyId, DifferenceDate, Source, BankStatementLinePartId, CurrencyCode, ForeignAmount, CarryingAmountRon,
        SettlementAmountRon, DifferenceRon, GlAccountCode, CreatedBy)
    OUTPUT inserted.Id INTO @FxOut (Id)
    SELECT @CompanyId, CASE WHEN a.BookingDate >= b.BookingDate THEN a.BookingDate ELSE b.BookingDate END, 2, pr.PartId,
        CASE WHEN inn.CurrencyCode <> 'RON' THEN inn.CurrencyCode ELSE outp.CurrencyCode END,
        CASE WHEN inn.CurrencyCode <> 'RON' THEN ABS(inn.Amount) ELSE ABS(outp.Amount) END,
        ABS(outp.Ron), ABS(inn.Ron), a.Ron + b.Ron, CASE WHEN a.Ron + b.Ron > 0 THEN N'765' ELSE N'665' END, @UserId
    FROM @Pairs AS pr
    JOIN @Values AS a ON a.PartId = pr.PartId
    JOIN @Values AS b ON b.PartId = pr.PairPartId
    CROSS APPLY (SELECT CASE WHEN a.Amount > 0 THEN a.CurrencyCode ELSE b.CurrencyCode END AS CurrencyCode,
                        CASE WHEN a.Amount > 0 THEN a.Amount ELSE b.Amount END AS Amount, CASE WHEN a.Amount > 0 THEN a.Ron ELSE b.Ron END AS Ron) AS inn
    CROSS APPLY (SELECT CASE WHEN a.Amount > 0 THEN b.CurrencyCode ELSE a.CurrencyCode END AS CurrencyCode,
                        CASE WHEN a.Amount > 0 THEN b.Amount ELSE a.Amount END AS Amount, CASE WHEN a.Amount > 0 THEN b.Ron ELSE a.Ron END AS Ron) AS outp
    WHERE a.Ron + b.Ron <> 0 AND SIGN(a.Amount) = -SIGN(b.Amount)
      -- O singura diferenta pe pereche, cand ambele linii sunt in lot.
      AND NOT (pr.PairPartId IN (SELECT PartId FROM @Pairs) AND pr.PairPartId < pr.PartId);

    SELECT x.Id, x.DifferenceDate, x.Source, x.AllocationId, x.PartnerId, x.Role, x.OpenItemId, x.CurrencyCode, x.ForeignAmount, x.DifferenceRon, x.IsIndexed,
        x.GlAccountCode, x.IsReversal
    FROM fin.FxDifference AS x JOIN @FxOut AS f ON f.Id = x.Id;
END;
