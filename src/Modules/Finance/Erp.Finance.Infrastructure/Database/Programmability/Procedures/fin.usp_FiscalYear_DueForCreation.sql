CREATE OR ALTER PROCEDURE fin.usp_FiscalYear_DueForCreation
    @Today date
AS
BEGIN
    -- Anii de creat de jobul de perioade (C3 3.1): pentru fiecare firma cu ani financiari, anul urmator, cand ultimul se termina in cel mult 60
    -- de zile; cu aceeasi lungime de 12 luni si periodicitatea de TVA a ultimului an.
    SET NOCOUNT ON;

    SELECT y.CompanyId, CAST(y.[Year] + 1 AS smallint) AS [Year], DATEADD(DAY, 1, y.EndDate) AS StartDate,
           EOMONTH(DATEADD(MONTH, 11, DATEADD(DAY, 1, y.EndDate))) AS EndDate, y.VatPeriodicity
    FROM fin.FiscalYear AS y
    WHERE y.[Year] = (SELECT MAX(x.[Year]) FROM fin.FiscalYear AS x WHERE x.CompanyId = y.CompanyId)
      AND DATEDIFF(DAY, @Today, y.EndDate) <= 60;
END;
