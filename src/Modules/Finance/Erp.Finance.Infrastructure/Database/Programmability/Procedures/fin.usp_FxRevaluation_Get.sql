CREATE OR ALTER PROCEDURE fin.usp_FxRevaluation_Get
    @CompanyId int,
    @RunId     int
AS
BEGIN
    -- Fisa rularii (macheta f4-01): antetul, cursurile folosite, totalurile pe moneda si liniile, cele mai mari diferente intai (cel mult 1000).
    SET NOCOUNT ON;

    SELECT r.Id, r.Number, r.PeriodYear, r.PeriodMonth, r.RevaluationDate, r.RateDate, r.Status, r.IncludesAdvances, r.TotalGainRon, r.TotalLossRon,
        r.GeneratedAt, r.ValidatedAt, uv.DisplayName AS ValidatedByName, r.ReversedAt, r.RowVersion,
        (SELECT COUNT(*) FROM fin.FxRevaluationLine AS l WHERE l.RunId = r.Id) AS LineCount
    FROM fin.FxRevaluationRun AS r
    LEFT JOIN core.[User] AS uv ON uv.Id = r.ValidatedBy
    WHERE r.Id = @RunId AND r.CompanyId = @CompanyId;

    SELECT x.CurrencyCode, x.Rate, x.Multiplier, x.RateDate, x.Source
    FROM fin.FxRevaluationRate AS x JOIN fin.FxRevaluationRun AS r ON r.Id = x.RunId
    WHERE x.RunId = @RunId AND r.CompanyId = @CompanyId
    ORDER BY x.CurrencyCode;

    SELECT l.CurrencyCode, COUNT(*) AS Lines, SUM(l.ForeignAmount) AS ForeignAmount, SUM(l.CarryingAmountRon) AS CarryingAmountRon,
        SUM(l.RevaluedAmountRon) AS RevaluedAmountRon, SUM(CASE WHEN l.DifferenceRon > 0 THEN l.DifferenceRon ELSE 0 END) AS GainRon,
        SUM(CASE WHEN l.DifferenceRon < 0 THEN -l.DifferenceRon ELSE 0 END) AS LossRon
    FROM fin.FxRevaluationLine AS l JOIN fin.FxRevaluationRun AS r ON r.Id = l.RunId
    WHERE l.RunId = @RunId AND r.CompanyId = @CompanyId
    GROUP BY l.CurrencyCode
    ORDER BY l.CurrencyCode;

    SELECT TOP (1000) l.Id, l.ElementKind, l.OpenItemId, o.DocumentNo, l.PartnerId, p.Name AS PartnerName, l.Role, l.CurrencyCode, l.ForeignAmount, l.Rate,
        l.CarryingAmountRon, l.RevaluedAmountRon, l.DifferenceRon, l.GlAccountCode, l.CounterGlAccountCode
    FROM fin.FxRevaluationLine AS l
    JOIN fin.FxRevaluationRun AS r ON r.Id = l.RunId
    LEFT JOIN fin.OpenItem AS o ON o.Id = l.OpenItemId
    LEFT JOIN core.Partner AS p ON p.Id = l.PartnerId
    WHERE l.RunId = @RunId AND r.CompanyId = @CompanyId
    ORDER BY ABS(l.DifferenceRon) DESC, l.Id;
END;
