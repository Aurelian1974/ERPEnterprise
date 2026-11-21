CREATE OR ALTER PROCEDURE core.usp_ExchangeRate_SaveManual
    @Id           int = NULL,
    @CurrencyCode char(3),
    @RateDate     date,
    @Rate         decimal(19,6),
    @Multiplier   int,
    @Note         nvarchar(500),
    @RowVersion   binary(8) = NULL,
    @UserId       int
AS
BEGIN
    -- Cursul manual (02.01, 3.5; macheta 07b): pentru contracte cu curs fix sau rapoarte interne, cu motiv obligatoriu. Nu inlocuieste
    -- cursul BNR pe documentele fiscale. Un curs BNR nu se modifica de aici (50008). Unic pe moneda si zi (50018).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Id IS NOT NULL AND EXISTS (SELECT 1 FROM core.ExchangeRate WHERE Id = @Id AND Source = 'BNR')
        THROW 50008, N'Cursul BNR nu se modifică manual.', 1;
    IF @CurrencyCode = 'RON' OR NOT EXISTS (SELECT 1 FROM core.Currency WHERE Code = @CurrencyCode AND IsActive = 1)
        THROW 50021, N'Alegeți o monedă activă, alta decât leul.', 1;
    IF EXISTS (SELECT 1 FROM core.ExchangeRate WITH (UPDLOCK, HOLDLOCK)
               WHERE CurrencyCode = @CurrencyCode AND RateDate = @RateDate AND Source = 'MANUAL' AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Există deja un curs manual pentru moneda și ziua alese.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.ExchangeRate (CurrencyCode, RateDate, Rate, Multiplier, Source, Note, CreatedBy)
        VALUES (@CurrencyCode, @RateDate, @Rate, @Multiplier, 'MANUAL', @Note, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.ExchangeRate
        SET CurrencyCode = @CurrencyCode, RateDate = @RateDate, Rate = @Rate, Multiplier = @Multiplier, Note = @Note,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND Source = 'MANUAL' AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.ExchangeRate WHERE Id = @Id)
                THROW 50027, N'Cursul nu există.', 1;
            THROW 50006, N'Cursul a fost modificat de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.ExchangeRate WHERE Id = @Id;
END;
