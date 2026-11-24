CREATE OR ALTER PROCEDURE fin.usp_CashFlow_Companies
AS
BEGIN
    -- Firmele pentru joburile de cash-flow (F5 3.6, 3.7): cele cu conturi incluse in prognoza; instantaneul automat (Fin.Forecast.AutoSnapshot,
    -- implicit pornit) si daca au reguli de alerta active.
    SET NOCOUNT ON;

    SELECT c.Id AS CompanyId,
           CAST(CASE WHEN s.Value IS NULL OR s.Value = N'true' THEN 1 ELSE 0 END AS bit) AS AutoSnapshot,
           CAST(CASE WHEN EXISTS (SELECT 1 FROM fin.CashFlowAlertRule AS r WHERE r.CompanyId = c.Id AND r.IsActive = 1) THEN 1 ELSE 0 END AS bit) AS HasAlertRules
    FROM core.Company AS c
    LEFT JOIN fin.CompanySetting AS s ON s.CompanyId = c.Id AND s.Code = 'Fin.Forecast.AutoSnapshot'
    WHERE EXISTS (SELECT 1 FROM fin.TreasuryAccount AS a WHERE a.CompanyId = c.Id AND a.IsActive = 1 AND a.IncludeInForecast = 1)
    ORDER BY c.Id;
END;
