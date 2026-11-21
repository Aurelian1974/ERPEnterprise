CREATE OR ALTER PROCEDURE core.usp_ExchangeRate_Get
    @CurrencyCode char(3),
    @Date         date,
    @Source       varchar(10) = 'BNR'
AS
BEGIN
    -- Cursul unei monede la o data (02.01, 4.3; public pentru module): cursul zilei sau, daca ziua nu are curs (weekend, sarbatoare,
    -- inainte de ora publicarii), ultimul curs anterior, cu data lui. RON e moneda de baza: cursul 1, la data ceruta.
    -- Fara niciun curs pana la data ceruta: 50007.
    SET NOCOUNT ON;

    IF @CurrencyCode = 'RON'
    BEGIN
        SELECT CAST('RON' AS char(3)) AS Currency, @Date AS RateDate, CAST(1 AS decimal(19,6)) AS Rate, 1 AS Multiplier, @Source AS Source;
        RETURN;
    END;

    IF NOT EXISTS (SELECT 1 FROM core.ExchangeRate WHERE CurrencyCode = @CurrencyCode AND Source = @Source AND RateDate <= @Date)
        THROW 50007, N'Nu există curs pentru moneda cerută la această dată sau înainte de ea.', 1;

    SELECT TOP (1) r.CurrencyCode AS Currency, r.RateDate, r.Rate, r.Multiplier, r.Source
    FROM core.ExchangeRate AS r
    WHERE r.CurrencyCode = @CurrencyCode AND r.Source = @Source AND r.RateDate <= @Date
    ORDER BY r.RateDate DESC;
END;
