CREATE OR ALTER PROCEDURE fin.usp_FxRevaluation_Inputs
    @CompanyId        int,
    @RevaluationDate  date,
    @IncludeAdvances  bit
AS
BEGIN
    -- Datele de pornire ale unei rulari (F4 4.4 pasul 1): monedele partidelor de reevaluat la data (in valuta, monetare sau si avansurile,
    -- cu rest diferit de zero la data) si ultima luna cu rulare validata a firmei.
    SET NOCOUNT ON;

    SELECT DISTINCT o.CurrencyCode
    FROM fin.OpenItem AS o
    WHERE o.CompanyId = @CompanyId AND o.CurrencyCode <> 'RON' AND o.DocumentDate <= @RevaluationDate AND (o.IsMonetary = 1 OR @IncludeAdvances = 1)
      AND (o.OpenAmount <> 0 OR EXISTS (SELECT 1 FROM fin.Allocation AS a
                                        WHERE a.Status = 1 AND a.AllocationDate > @RevaluationDate AND (a.DebitOpenItemId = o.Id OR a.CreditOpenItemId = o.Id)))
    UNION
    -- Conturile de trezorerie in valuta cu miscari pana la data (F4 3.4).
    SELECT a.CurrencyCode FROM fin.TreasuryAccount AS a
    WHERE a.CompanyId = @CompanyId AND a.CurrencyCode <> 'RON'
      AND (EXISTS (SELECT 1 FROM fin.BankStatementLine AS l WHERE l.TreasuryAccountId = a.Id AND l.BookingDate <= @RevaluationDate)
           OR EXISTS (SELECT 1 FROM fin.BankStatement AS s WHERE s.TreasuryAccountId = a.Id AND s.PeriodFrom <= @RevaluationDate AND s.OpeningBalance <> 0)
           OR EXISTS (SELECT 1 FROM fin.CashDocument AS c WHERE c.TreasuryAccountId = a.Id AND c.Status IN (2, 3) AND c.DocumentDate <= @RevaluationDate));

    SELECT TOP (1) PeriodYear, PeriodMonth FROM fin.FxRevaluationRun
    WHERE CompanyId = @CompanyId AND Status = 2
    ORDER BY PeriodYear DESC, PeriodMonth DESC;
END;
