CREATE OR ALTER PROCEDURE core.usp_ExchangeRate_SaveBatch
    @Rates  core.ExchangeRateList READONLY,
    @UserId int
AS
BEGIN
    -- Cursurile BNR (02.01, 4.3): se adauga doar zilele si monedele care lipsesc; un curs BNR salvat nu se mai schimba (preluarea se
    -- poate repeta fara efect). O moneda publicata de BNR si absenta din nomenclator se adauga inactiva, cu codul ca denumire.
    -- Intoarce cursurile adaugate (pentru evenimentul CursValutarActualizat). Ruleaza in tranzactia comenzii.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM @Rates WHERE Rate <= 0 OR Multiplier <= 0 OR CurrencyCode NOT LIKE '[A-Z][A-Z][A-Z]')
        THROW 50021, N'Un curs BNR are moneda, valoarea sau multiplicatorul invalid.', 1;

    INSERT INTO core.Currency (Code, Name, Decimals, IsActive, CreatedBy)
    SELECT DISTINCT r.CurrencyCode, r.CurrencyCode, 2, 0, @UserId
    FROM @Rates AS r
    WHERE r.CurrencyCode <> 'RON' AND NOT EXISTS (SELECT 1 FROM core.Currency AS c WITH (UPDLOCK, HOLDLOCK) WHERE c.Code = r.CurrencyCode);

    DECLARE @Added TABLE (CurrencyCode char(3), RateDate date, Rate decimal(19,6), Multiplier int);
    INSERT INTO core.ExchangeRate (CurrencyCode, RateDate, Rate, Multiplier, Source, CreatedBy)
    OUTPUT inserted.CurrencyCode, inserted.RateDate, inserted.Rate, inserted.Multiplier INTO @Added
    SELECT r.CurrencyCode, r.RateDate, r.Rate, r.Multiplier, 'BNR', @UserId
    FROM @Rates AS r
    WHERE r.CurrencyCode <> 'RON'
      AND NOT EXISTS (SELECT 1 FROM core.ExchangeRate AS e WITH (UPDLOCK, HOLDLOCK)
                      WHERE e.CurrencyCode = r.CurrencyCode AND e.RateDate = r.RateDate AND e.Source = 'BNR');

    SELECT CurrencyCode, RateDate, Rate, Multiplier FROM @Added ORDER BY RateDate, CurrencyCode;
END;
