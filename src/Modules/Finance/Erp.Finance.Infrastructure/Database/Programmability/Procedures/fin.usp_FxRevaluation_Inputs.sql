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
                                        WHERE a.Status = 1 AND a.AllocationDate > @RevaluationDate AND (a.DebitOpenItemId = o.Id OR a.CreditOpenItemId = o.Id)));

    SELECT TOP (1) PeriodYear, PeriodMonth FROM fin.FxRevaluationRun
    WHERE CompanyId = @CompanyId AND Status = 2
    ORDER BY PeriodYear DESC, PeriodMonth DESC;
END;
