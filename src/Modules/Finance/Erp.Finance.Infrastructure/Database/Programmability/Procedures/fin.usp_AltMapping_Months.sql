CREATE OR ALTER PROCEDURE fin.usp_AltMapping_Months
    @ChartId  int,
    @From     date,
    @To       date
AS
BEGIN
    -- Lunile cu note validate ale firmelor planului alternativ in interval (C1 4.4 pasul 1), cu numarul de linii si inghetarea fiecareia.
    SET NOCOUNT ON;

    SELECT l.CompanyId, l.PeriodYear, l.PeriodMonth, COUNT(*) AS LineCount,
        CAST(CASE WHEN EXISTS (SELECT 1 FROM fin.AltChartPeriodFreeze AS f WHERE f.ChartId = @ChartId AND f.CompanyId = l.CompanyId
                                AND f.PeriodYear = l.PeriodYear AND f.PeriodMonth = l.PeriodMonth) THEN 1 ELSE 0 END AS bit) AS IsFrozen
    FROM fin.JournalLine AS l
    JOIN fin.JournalEntry AS e ON e.Id = l.EntryId AND e.Status >= 2
    JOIN fin.ChartOfAccountsCompany AS cc ON cc.ChartId = @ChartId AND cc.CompanyId = l.CompanyId
    WHERE e.AccountingDate BETWEEN @From AND @To
    GROUP BY l.CompanyId, l.PeriodYear, l.PeriodMonth
    ORDER BY l.CompanyId, l.PeriodYear, l.PeriodMonth;
END;
