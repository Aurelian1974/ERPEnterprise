CREATE OR ALTER PROCEDURE fin.usp_FxRevaluation_List
    @CompanyId int,
    @Year      smallint = NULL
AS
BEGIN
    -- Rularile de reevaluare ale firmei, cele mai noi intai (macheta f4-01), cu numarul de linii si daca au aparut alocari in luna dupa
    -- generare (ciorna de regenerat).
    SET NOCOUNT ON;

    SELECT TOP (120) r.Id, r.Number, r.PeriodYear, r.PeriodMonth, r.RevaluationDate, r.RateDate, r.Status, r.TotalGainRon, r.TotalLossRon,
        (SELECT ISNULL(SUM(l.DifferenceRon), 0) FROM fin.FxRevaluationLine AS l WHERE l.RunId = r.Id) AS NetRon,
        (SELECT COUNT(*) FROM fin.FxRevaluationLine AS l WHERE l.RunId = r.Id) AS LineCount,
        CAST(CASE WHEN r.Status = 1 AND EXISTS (SELECT 1 FROM fin.Allocation AS a WHERE a.CompanyId = r.CompanyId AND a.AllocationDate <= r.RevaluationDate
                                                AND (a.CreatedAt > r.GeneratedAt OR a.UndoneAt > r.GeneratedAt))
                   THEN 1 ELSE 0 END AS bit) AS IsStale,
        r.GeneratedAt, r.ValidatedAt, r.ReversedAt
    FROM fin.FxRevaluationRun AS r
    WHERE r.CompanyId = @CompanyId AND (@Year IS NULL OR r.PeriodYear = @Year)
    ORDER BY r.PeriodYear DESC, r.PeriodMonth DESC, r.Id DESC;
END;
