CREATE OR ALTER PROCEDURE core.usp_ExchangeRate_Get
    @CurrencyCode char(3),
    @Date         date,
    @Source       varchar(10) = 'BNR',
    @Closing      bit = 0
AS
BEGIN
    -- Cursul unei monede la o data (02.01, 4.3; public pentru module), cu data lui. Pentru operatiunile zilei (@Closing = 0): ultimul
    -- curs cu data strict anterioara (cursul publicat de BNR vineri dupa-amiaza se foloseste sambata, duminica si luni; Codul fiscal,
    -- art. 290). Pentru reevaluare si solduri la o data (@Closing = 1): ultimul curs pana la data ceruta, inclusiv.
    -- RON e moneda de baza: cursul 1, la data ceruta. Fara niciun curs: 50007.
    SET NOCOUNT ON;

    IF @CurrencyCode = 'RON'
    BEGIN
        SELECT CAST('RON' AS char(3)) AS Currency, @Date AS RateDate, CAST(1 AS decimal(19,6)) AS Rate, 1 AS Multiplier, @Source AS Source;
        RETURN;
    END;

    DECLARE @Last date = CASE WHEN @Closing = 1 THEN @Date ELSE DATEADD(DAY, -1, @Date) END;

    IF NOT EXISTS (SELECT 1 FROM core.ExchangeRate WHERE CurrencyCode = @CurrencyCode AND Source = @Source AND RateDate <= @Last)
        THROW 50007, N'Nu există curs BNR pentru moneda cerută înainte de această dată.', 1;

    SELECT TOP (1) r.CurrencyCode AS Currency, r.RateDate, r.Rate, r.Multiplier, r.Source
    FROM core.ExchangeRate AS r
    WHERE r.CurrencyCode = @CurrencyCode AND r.Source = @Source AND r.RateDate <= @Last
    ORDER BY r.RateDate DESC;
END;
