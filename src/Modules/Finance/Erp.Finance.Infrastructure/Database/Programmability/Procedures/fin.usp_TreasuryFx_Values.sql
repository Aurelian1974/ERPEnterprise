CREATE OR ALTER PROCEDURE fin.usp_TreasuryFx_Values
    @CompanyId  int,
    @AsOf       date,
    @AccountId  int = NULL
AS
BEGIN
    -- Valoarea contabila in lei a conturilor de trezorerie in valuta la o data (F4 3.4): soldul in valuta si valoarea lui in lei = soldul de
    -- deschidere si fiecare miscare la cursul BNR al zilei ei (liniile de extras; documentele de casa validate si stornate, cu valoarea lor in
    -- lei) plus diferentele reevaluarilor validate pana la data. MissingRates = cate miscari nu au curs (valoarea in lei nu e completa).
    SET NOCOUNT ON;

    SELECT a.Id AS TreasuryAccountId, a.Code, a.Kind, a.CurrencyCode,
        ISNULL(a.GlAccountCode, CASE WHEN a.Kind = 2 THEN N'5314' ELSE N'5124' END) AS GlAccountCode,
        ISNULL(bo.Opening, 0) + ISNULL(bl.Amount, 0) + ISNULL(co.Opening, 0) + ISNULL(cd.Amount, 0) AS Balance,
        ISNULL(bo.OpeningRon, 0) + ISNULL(bl.AmountRon, 0) + ISNULL(co.OpeningRon, 0) + ISNULL(cd.AmountRon, 0) + ISNULL(rv.Ron, 0) AS CarryingAmountRon,
        ISNULL(bl.Missing, 0) + CASE WHEN bo.Opening <> 0 AND bo.OpeningRon IS NULL THEN 1 ELSE 0 END
            + CASE WHEN co.Opening <> 0 AND co.OpeningRon IS NULL THEN 1 ELSE 0 END AS MissingRates
    FROM fin.TreasuryAccount AS a
    -- Soldul de deschidere al primului extras, la cursul zilei dinaintea lui.
    OUTER APPLY (SELECT TOP (1) s.OpeningBalance AS Opening, ROUND(s.OpeningBalance * r.PerUnit, 2) AS OpeningRon
                 FROM fin.BankStatement AS s
                 OUTER APPLY (SELECT TOP (1) x.Rate / x.Multiplier AS PerUnit FROM core.ExchangeRate AS x
                              WHERE x.CurrencyCode = a.CurrencyCode AND x.RateDate < s.PeriodFrom ORDER BY x.RateDate DESC) AS r
                 WHERE s.TreasuryAccountId = a.Id AND s.PeriodFrom <= @AsOf
                 ORDER BY s.PeriodFrom, s.Id) AS bo
    OUTER APPLY (SELECT SUM(l.Amount) AS Amount, SUM(ROUND(l.Amount * r.PerUnit, 2)) AS AmountRon, SUM(CASE WHEN r.PerUnit IS NULL THEN 1 ELSE 0 END) AS Missing
                 FROM fin.BankStatementLine AS l
                 OUTER APPLY (SELECT TOP (1) x.Rate / x.Multiplier AS PerUnit FROM core.ExchangeRate AS x
                              WHERE x.CurrencyCode = a.CurrencyCode AND x.RateDate <= l.BookingDate ORDER BY x.RateDate DESC) AS r
                 WHERE l.TreasuryAccountId = a.Id AND l.BookingDate <= @AsOf) AS bl
    OUTER APPLY (SELECT TOP (1) d.OpeningBalance AS Opening, ROUND(d.OpeningBalance * r.PerUnit, 2) AS OpeningRon
                 FROM fin.CashRegisterDay AS d
                 OUTER APPLY (SELECT TOP (1) x.Rate / x.Multiplier AS PerUnit FROM core.ExchangeRate AS x
                              WHERE x.CurrencyCode = a.CurrencyCode AND x.RateDate < d.[Day] ORDER BY x.RateDate DESC) AS r
                 WHERE d.TreasuryAccountId = a.Id AND d.[Day] <= @AsOf
                 ORDER BY d.[Day]) AS co
    OUTER APPLY (SELECT SUM(CASE WHEN c.Kind = 3 THEN -c.TotalAmount ELSE c.TotalAmount END) AS Amount,
                        SUM(CASE WHEN c.Kind = 3 THEN -c.TotalAmountRon ELSE c.TotalAmountRon END) AS AmountRon
                 FROM fin.CashDocument AS c
                 WHERE c.TreasuryAccountId = a.Id AND c.Status IN (2, 3) AND c.DocumentDate <= @AsOf) AS cd
    OUTER APPLY (SELECT SUM(l.DifferenceRon) AS Ron FROM fin.FxRevaluationLine AS l
                 JOIN fin.FxRevaluationRun AS r ON r.Id = l.RunId AND r.Status = 2 AND r.RevaluationDate <= @AsOf
                 WHERE l.TreasuryAccountId = a.Id) AS rv
    WHERE a.CompanyId = @CompanyId AND a.CurrencyCode <> 'RON' AND (@AccountId IS NULL OR a.Id = @AccountId)
    ORDER BY a.Code;
END;
