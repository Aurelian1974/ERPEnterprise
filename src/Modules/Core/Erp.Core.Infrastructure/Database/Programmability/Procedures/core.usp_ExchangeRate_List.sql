CREATE OR ALTER PROCEDURE core.usp_ExchangeRate_List
    @From date,
    @To   date
AS
BEGIN
    -- Configurare › Cursuri valutare (macheta 07b). Intoarce, in ordine: valutele; cursurile BNR din interval; ultimul curs BNR de
    -- dinaintea intervalului pe fiecare moneda (pentru zilele fara curs de la inceput); cursurile manuale din interval, cu autorul;
    -- ultima preluare BNR (momentul si ziua cursului).
    SET NOCOUNT ON;

    SELECT c.Code, c.Name, c.Decimals, c.IsActive, c.RowVersion
    FROM core.Currency AS c
    ORDER BY CASE WHEN c.Code = 'RON' THEN 0 ELSE 1 END, c.IsActive DESC, c.Code;

    SELECT r.CurrencyCode, r.RateDate, r.Rate, r.Multiplier, r.CreatedAt
    FROM core.ExchangeRate AS r
    WHERE r.Source = 'BNR' AND r.RateDate BETWEEN @From AND @To
    ORDER BY r.RateDate DESC, r.CurrencyCode;

    SELECT p.CurrencyCode, p.RateDate, p.Rate, p.Multiplier, p.CreatedAt
    FROM (
        SELECT r.CurrencyCode, r.RateDate, r.Rate, r.Multiplier, r.CreatedAt,
               ROW_NUMBER() OVER (PARTITION BY r.CurrencyCode ORDER BY r.RateDate DESC) AS Rn
        FROM core.ExchangeRate AS r
        WHERE r.Source = 'BNR' AND r.RateDate < @From
    ) AS p
    WHERE p.Rn = 1;

    SELECT r.Id, r.CurrencyCode, r.RateDate, r.Rate, r.Multiplier, r.Note, COALESCE(m.DisplayName, u.DisplayName) AS ChangedByName,
           COALESCE(r.ModifiedAt, r.CreatedAt) AS ChangedAt, r.RowVersion
    FROM core.ExchangeRate AS r
    JOIN core.[User] AS u ON u.Id = r.CreatedBy
    LEFT JOIN core.[User] AS m ON m.Id = r.ModifiedBy
    WHERE r.Source = 'MANUAL' AND r.RateDate BETWEEN @From AND @To
    ORDER BY r.RateDate DESC, r.CurrencyCode;

    SELECT MAX(r.CreatedAt) AS LastImportAt, MAX(r.RateDate) AS LastRateDate
    FROM core.ExchangeRate AS r
    WHERE r.Source = 'BNR';
END;
